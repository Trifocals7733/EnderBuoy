using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace EnderBuoy;

public class Blink : MonoBehaviour
{
    static ConfigEntry<bool> _enabled, _blink, _requireLit, _diagnostics, _playSound, _spawnSmoke;
    static ConfigEntry<float> _maxDist, _timeout;
    static ConfigEntry<KeyCode> _key;

    public static void Bind(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> blink,
        ConfigEntry<bool> requireLit,
        ConfigEntry<float> maxDist,
        ConfigEntry<float> timeout,
        ConfigEntry<bool> diagnostics,
        ConfigEntry<KeyCode> key,
        ConfigEntry<bool> playSound,
        ConfigEntry<bool> spawnSmoke)
    {
        _enabled = enabled;
        _blink = blink;
        _requireLit = requireLit;
        _maxDist = maxDist;
        _timeout = timeout;
        _diagnostics = diagnostics;
        _key = key;
        _playSound = playSound;
        _spawnSmoke = spawnSmoke;
    }

    public static bool Armed => _enabled != null && _blink != null && _enabled.Value && _blink.Value;

    // Tracking state
    static bool _tracking;
    static Prop _trackedProp;
    static Transform _trackedTransform;
    static Rigidbody _trackedRb;
    static Vector3 _releasePoint;
    static float _releaseTime;
    static float _ignoreUntil;
    static float _prevSpeed;
    static float _lastSafeY = float.MinValue;
    static float _nextDiag;

    // Smoke template
    static GameObject _smokeTemplate;

    // Multiplayer friend teleport tracking
    static readonly System.Collections.Generic.Dictionary<int, Vector3> _lastPlayerPositions = new();

    const float KillMargin = 30f;
    const float MinThrowSqrImpulse = 4.0f; // 2 m/s minimum throw force magnitude

    static bool IsBuoyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.IndexOf("buoy", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("bouy", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static Transform FindBouyLight(Transform root)
    {
        if (root == null) return null;
        try
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c == null) continue;
                string n = c.gameObject.name;
                if ((string.Equals(n, "bouyLight", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(n, "buoyLight", System.StringComparison.OrdinalIgnoreCase))
                    && c.GetComponent<Prop>() == null)
                {
                    return c;
                }
                var deep = FindBouyLight(c);
                if (deep != null) return deep;
            }
        }
        catch { }
        return null;
    }

    static bool IsLit(Prop prop, PlayerCharacter holder)
    {
        try
        {
            if (prop != null && prop.gameObject != null)
            {
                var bl = FindBouyLight(prop.gameObject.transform);
                if (bl != null) return bl.gameObject.activeSelf;
            }
        }
        catch { }

        try
        {
            var hand = holder != null ? FindDescendant(holder.transform, "grasperHand") : null;
            if (hand != null)
            {
                var bl = FindBouyLight(hand);
                if (bl != null) return bl.gameObject.activeSelf;
            }
        }
        catch { }

        return false;
    }

    static Transform FindDescendant(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = null;
            try { c = root.GetChild(i); } catch { continue; }
            if (c == null) continue;
            try { if (c.gameObject.name == name) return c; } catch { continue; }
            var deep = FindDescendant(c, name);
            if (deep != null) return deep;
        }
        return null;
    }

    static PlayerCharacter LocalPlayer()
    {
        try
        {
            var pc = WorldManager.localPlayerCharacter;
            if (pc != null) return pc;
        }
        catch { }

        try
        {
            var list = PlayerCharacter.allPlayerCharacters;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var pc = list[i];
                    if (pc != null && pc.playerNetworking != null && pc.playerNetworking.isLocalPlayer)
                        return pc;
                }
            }
        }
        catch { }
        return null;
    }

    static bool IsPlayerCollider(Collider col)
    {
        if (col == null) return false;
        try
        {
            if (col.GetComponentInParent<PlayerCharacter>() != null) return true;
        }
        catch { }
        return false;
    }

    internal static void OnThrown(Prop prop, PlayerCharacter holder, Vector3 throwForce)
    {
        if (!Armed) return;

        bool local = false;
        try { local = holder != null && holder.playerNetworking != null && holder.playerNetworking.isLocalPlayer; }
        catch { return; }
        if (!local) return;

        string name = "";
        try { name = prop != null && prop.gameObject != null ? prop.gameObject.name : ""; }
        catch { return; }
        if (!IsBuoyName(name)) return;

        if (throwForce.sqrMagnitude < MinThrowSqrImpulse)
        {
            if (_diagnostics != null && _diagnostics.Value)
            {
                Plugin.Log.LogInfo($"EnderBuoy: release ignored (gentle drop, force={throwForce.magnitude:F2}).");
            }
            return;
        }

        if (_tracking)
        {
            Plugin.Log.LogInfo("EnderBuoy: throw ignored, already tracking an active flight.");
            return;
        }

        bool lit = true;
        if (_requireLit != null && _requireLit.Value)
        {
            lit = IsLit(prop, holder);
        }

        Plugin.Log.LogInfo($"EnderBuoy: THROW prop={name} lit={lit} force={throwForce.magnitude:F1} pos={prop.gameObject.transform.position}");
        if (!lit)
        {
            Plugin.Log.LogInfo("EnderBuoy: throw ignored (unlit buoy).");
            return;
        }

        try
        {
            _trackedProp = prop;
            _trackedTransform = prop.gameObject.transform;
            _trackedRb = prop.GetComponent<Rigidbody>();
            _releasePoint = _trackedTransform.position;
            _releaseTime = Time.time;
            _ignoreUntil = Time.time + 0.05f; // 50ms initial clearance deadzone
            _prevSpeed = throwForce.magnitude;
            _nextDiag = Time.time + 1.0f;

            _lastSafeY = float.MinValue;
            try
            {
                var lp = LocalPlayer();
                var g = lp != null ? lp.ground : null;
                if (g != null) _lastSafeY = g.lastSafePosition.y;
            }
            catch { }

            _tracking = true;
            Plugin.Log.LogInfo($"EnderBuoy: tracking start release={_releasePoint} safeY={_lastSafeY}");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"EnderBuoy: error initializing throw tracking: {e}");
            _tracking = false;
            _trackedProp = null;
            _trackedTransform = null;
            _trackedRb = null;
        }
    }

    internal static void OnDroppedWithoutForce(Prop prop, PlayerCharacter holder)
    {
        // Gentle drops without throw impulse do not trigger teleport tracking
    }

    internal static void OnPropCollisionEnter(GameObject go, Collision col)
    {
        if (!_tracking || _trackedProp == null || go == null) return;
        if (Time.time < _ignoreUntil) return;

        if (go != _trackedProp.gameObject) return;

        // Ignore collisions with the player
        if (col != null && col.collider != null)
        {
            if (col.collider.gameObject == _trackedProp.gameObject) return;
            if (IsPlayerCollider(col.collider)) return;
        }

        Vector3 hitPoint = _trackedTransform != null ? _trackedTransform.position : go.transform.position;
        Vector3 hitNormal = Vector3.up;
        try
        {
            if (col != null && col.contactCount > 0)
            {
                var contact = col.GetContact(0);
                hitPoint = contact.point;
                hitNormal = contact.normal;
            }
        }
        catch { }

        TriggerImpact(hitPoint, hitNormal, "physical collision");
    }

    void FixedUpdate()
    {
        if (!_tracking || _trackedTransform == null || _trackedRb == null) return;
        if (Time.time < _ignoreUntil) return;

        Vector3 pos;
        try { pos = _trackedTransform.position; }
        catch { Cancel("prop transform lost"); return; }

        if (Time.time - _releaseTime > (_timeout != null ? _timeout.Value : 8f))
        {
            Cancel("timeout, no landing");
            return;
        }

        if (_lastSafeY > float.MinValue / 2f && pos.y < _lastSafeY - KillMargin)
        {
            Cancel("below kill plane");
            return;
        }

        float distXZ = Vector2.Distance(new Vector2(_releasePoint.x, _releasePoint.z), new Vector2(pos.x, pos.z));
        if (_maxDist != null && distXZ > _maxDist.Value)
        {
            Cancel($"beyond max distance in flight ({distXZ:F1} m)");
            return;
        }

        Vector3 vel = _trackedRb.velocity;
        float speed = vel.magnitude;

        if (_diagnostics != null && _diagnostics.Value && Time.time >= _nextDiag)
        {
            _nextDiag = Time.time + 1f;
            Plugin.Log.LogInfo($"EnderBuoy: tracking pos={pos} sp={speed:F2} dist={distXZ:F1} m");
        }

        // Secondary prediction: SphereCast along velocity vector for next physics step
        if (speed > 0.5f)
        {
            Vector3 dir = vel / speed;
            float stepDist = speed * Time.fixedDeltaTime + 0.15f;
            if (Physics.SphereCast(pos, 0.12f, dir, out RaycastHit hit, stepDist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.gameObject != _trackedProp.gameObject && !IsPlayerCollider(hit.collider))
                {
                    TriggerImpact(hit.point, hit.normal, "spherecast sweep");
                    return;
                }
            }
        }

        // Abrupt deceleration fallback (if collision occurred without PlatformingBody callback)
        if (_prevSpeed > 3.0f && speed < 1.0f)
        {
            if (Physics.Raycast(pos + Vector3.up * 0.1f, Vector3.down, out RaycastHit groundHit, 0.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (groundHit.collider != null && groundHit.collider.gameObject != _trackedProp.gameObject && !IsPlayerCollider(groundHit.collider))
                {
                    TriggerImpact(groundHit.point, groundHit.normal, "velocity halt fallback");
                    return;
                }
            }
        }

        _prevSpeed = speed;
    }

    static bool IsNearAnyBuoy(Vector3 pos, float maxDist)
    {
        try
        {
            var props = Prop.allProps;
            if (props != null && props.Count > 0)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (p != null && p.gameObject != null && IsBuoyName(p.gameObject.name))
                    {
                        if (Vector3.Distance(pos, p.transform.position) <= maxDist)
                            return true;
                    }
                }
                return false;
            }
        }
        catch { }
        return true;
    }

    static float _nextPlayerScan;
    static PlayerCharacter[] _cachedPlayers;

    void CheckRemoteTeleports()
    {
        try
        {
            var list = PlayerCharacter.allPlayerCharacters;
            if (list != null && list.Count > 0)
            {
                var local = LocalPlayer();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null || p.gameObject == null) continue;

                    int id = p.GetInstanceID();
                    Vector3 curr = p.transform.position;

                    // Local player effects are triggered directly inside TeleportPlayer
                    if (local != null && p == local)
                    {
                        _lastPlayerPositions[id] = curr;
                        continue;
                    }

                    if (_lastPlayerPositions.TryGetValue(id, out Vector3 prev))
                    {
                        float dist = Vector3.Distance(prev, curr);
                        // Single-frame displacement > 2.5m indicates a teleport rather than walking/running
                        if (dist > 2.5f && dist < 250f)
                        {
                            if (IsNearAnyBuoy(curr, 6.0f))
                            {
                                Plugin.Log.LogInfo($"EnderBuoy: observed remote friend teleport ({p.name}, {dist:F1} m)");
                                SpawnSmoke(prev, p);
                                SpawnSmoke(curr, p);
                                PlayTeleportSound(curr, p);
                                PlayTeleportSound(prev, p);
                            }
                        }
                    }
                    _lastPlayerPositions[id] = curr;
                }
                return;
            }

            // Fallback only if allPlayerCharacters is empty or uninitialized
            if (Time.time >= _nextPlayerScan)
            {
                _nextPlayerScan = Time.time + 2.0f;
                _cachedPlayers = UnityEngine.Object.FindObjectsOfType<PlayerCharacter>();
            }

            if (_cachedPlayers != null)
            {
                var local = LocalPlayer();
                for (int i = 0; i < _cachedPlayers.Length; i++)
                {
                    var p = _cachedPlayers[i];
                    if (p == null || p.gameObject == null) continue;

                    int id = p.GetInstanceID();
                    Vector3 curr = p.transform.position;

                    if (local != null && p == local)
                    {
                        _lastPlayerPositions[id] = curr;
                        continue;
                    }

                    if (_lastPlayerPositions.TryGetValue(id, out Vector3 prev))
                    {
                        float dist = Vector3.Distance(prev, curr);
                        if (dist > 2.5f && dist < 250f)
                        {
                            if (IsNearAnyBuoy(curr, 6.0f))
                            {
                                Plugin.Log.LogInfo($"EnderBuoy: observed remote friend teleport ({p.name}, {dist:F1} m)");
                                SpawnSmoke(prev, p);
                                SpawnSmoke(curr, p);
                                PlayTeleportSound(curr, p);
                                PlayTeleportSound(prev, p);
                            }
                        }
                    }
                    _lastPlayerPositions[id] = curr;
                }
            }
        }
        catch { }
    }

    void Update()
    {
        if (_enabled != null && _enabled.Value && _key != null && _key.Value != KeyCode.None && Input.GetKeyDown(_key.Value))
        {
            if (_blink != null)
            {
                _blink.Value = !_blink.Value;
                Plugin.Log.LogInfo($"EnderBuoy: Blink {(_blink.Value ? "on" : "off")}");
                if (!_blink.Value && _tracking) Cancel("disarmed mid-flight");
            }
        }
        if (_enabled != null && !_enabled.Value && _tracking)
        {
            Cancel("mod disabled mid-flight");
        }

        CheckRemoteTeleports();
    }

    static bool IsPropOrPlayerCollider(Collider col, Prop prop)
    {
        if (col == null) return false;
        try
        {
            if (IsPlayerCollider(col)) return true;
            if (prop != null)
            {
                if (col.gameObject == prop.gameObject) return true;
                if (col.GetComponentInParent<Prop>() == prop) return true;
            }
        }
        catch { }
        return false;
    }

    const float PlayerStandingVerticalOffset = 0.70f; // 0.50m ground-to-pivot + 0.20m safe drop clearance

    static Vector3 CalculateLandingPosition(Vector3 contactPoint, Vector3 normal, Prop prop, Vector3 buoyVelocity)
    {
        Vector3 up = Vector3.up;
        Vector3 norm = normal.sqrMagnitude > 0.01f ? normal.normalized : up;
        float slopeDot = Vector3.Dot(norm, up);

        Vector3 target;

        if (slopeDot >= 0.5f) // Walkable ground or gentle slope (< 60 degrees)
        {
            // Normal lift + vertical height offset so feet land safely above the surface
            target = contactPoint + norm * 0.15f + up * PlayerStandingVerticalOffset;
        }
        else // Steep wall, rock face, ledge, or vertical obstacle
        {
            // Push horizontally away from the wall by 0.65m (player body radius is ~0.4m)
            Vector3 wallOut = new Vector3(norm.x, 0f, norm.z);
            if (wallOut.sqrMagnitude < 0.01f)
            {
                wallOut = new Vector3(-buoyVelocity.x, 0f, -buoyVelocity.z);
            }
            if (wallOut.sqrMagnitude < 0.01f)
            {
                wallOut = Vector3.forward;
            }
            wallOut = wallOut.normalized;

            Vector3 testPos = contactPoint + wallOut * 0.65f;

            // Probe downwards to see if solid ground exists within 4.5 meters below the wall contact
            if (Physics.Raycast(testPos + up * 0.5f, Vector3.down, out RaycastHit groundHit, 4.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (groundHit.collider != null && !IsPropOrPlayerCollider(groundHit.collider, prop))
                {
                    target = groundHit.point + up * PlayerStandingVerticalOffset;
                }
                else
                {
                    target = testPos + up * PlayerStandingVerticalOffset;
                }
            }
            else
            {
                target = testPos + up * PlayerStandingVerticalOffset;
            }
        }

        // Downward ground check: ensure target is never below nearby terrain
        if (Physics.Raycast(target + up * 1.0f, Vector3.down, out RaycastHit surfaceHit, 1.8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (surfaceHit.collider != null && !IsPropOrPlayerCollider(surfaceHit.collider, prop))
            {
                float minSafeY = surfaceHit.point.y + PlayerStandingVerticalOffset;
                if (minSafeY > target.y)
                {
                    target.y = minSafeY;
                }
            }
        }

        return target;
    }

    static void TriggerImpact(Vector3 contactPoint, Vector3 normal, string source)
    {
        if (!_tracking) return;
        _tracking = false; // Disarm immediately to prevent re-entrant calls

        var player = LocalPlayer();
        if (player == null)
        {
            Cancel("local player lost");
            return;
        }

        try
        {
            Vector3 fromPos = player.transform.position;
            Vector3 buoyVel = _trackedRb != null ? _trackedRb.velocity : Vector3.zero;

            // Robust landing position preventing feet embedding or spawning inside geometry
            Vector3 target = CalculateLandingPosition(contactPoint, normal, _trackedProp, buoyVel);

            // 1. Audio: play random throwSound cue at destination
            PlayTeleportSound(target, player);

            // 2. VFX: Smoke puffs that linger for 3s at departure and arrival locations
            SpawnSmoke(fromPos + Vector3.up * 0.5f, player);
            SpawnSmoke(target + Vector3.up * 0.5f, player);

            // 3. Movement write
            var rb = player.rb;
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = target;
            }

            try { player.faller?.ClearNextFall(); } catch { }

            // Official game teleport routine
            if (player.grease != null)
            {
                player.grease.Teleport(target, player.transform.rotation, true);
            }

            player.transform.position = target;

            if (player.mover != null)
            {
                try { player.mover.cachedKernalPos = target; } catch { }
            }

            float dist = Vector3.Distance(fromPos, target);
            Plugin.Log.LogInfo($"EnderBuoy: teleported ({source}) {fromPos} -> {target} (dist {dist:F1} m)");
            try { _lastPlayerPositions[player.GetInstanceID()] = target; } catch { }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"EnderBuoy: teleport write failed: {e}");
        }
        finally
        {
            _trackedProp = null;
            _trackedTransform = null;
            _trackedRb = null;
        }
    }

    static void PlayTeleportSound(Vector3 targetPos, PlayerCharacter player)
    {
        if (_playSound == null || !_playSound.Value || player == null) return;
        try
        {
            var audioRefs = player.playerAudio;
            if (audioRefs == null) return;
            var container = audioRefs.throwSound;
            if (container == null || container.Cues == null || container.Cues.Length == 0) return;

            int idx = UnityEngine.Random.Range(0, container.Cues.Length);
            var cue = container.Cues[idx];
            if (cue != null && cue.Clip != null)
            {
                AudioSource.PlayClipAtPoint(cue.Clip, targetPos, 1.0f);
                if (_diagnostics != null && _diagnostics.Value)
                {
                    Plugin.Log.LogInfo($"EnderBuoy: played throwSound cue #{idx} ({cue.Clip.name})");
                }
            }
        }
        catch (System.Exception e)
        {
            if (_diagnostics != null && _diagnostics.Value)
            {
                Plugin.Log.LogInfo($"EnderBuoy: sound play error: {e.Message}");
            }
        }
    }

    static GameObject GetSmokeTemplate(PlayerCharacter player)
    {
        if (_smokeTemplate != null) return _smokeTemplate;

        // Try scene flare smoke
        try
        {
            var allPs = UnityEngine.Object.FindObjectsOfType<ParticleSystem>(true);
            if (allPs != null)
            {
                foreach (var ps in allPs)
                {
                    if (ps != null && ps.gameObject != null && ps.gameObject.name == "SmokeParticleSystem")
                    {
                        _smokeTemplate = ps.gameObject;
                        Plugin.Log.LogInfo("EnderBuoy: cached SmokeParticleSystem template from scene.");
                        return _smokeTemplate;
                    }
                }
            }
        }
        catch { }

        // Fallback to player's puffTorso
        try
        {
            if (player != null && player.looks != null && player.looks.puffTorso != null)
            {
                _smokeTemplate = player.looks.puffTorso.gameObject;
                Plugin.Log.LogInfo("EnderBuoy: cached player puffTorso template.");
                return _smokeTemplate;
            }
        }
        catch { }

        return null;
    }

    struct PlayerPalette
    {
        public Color32 Smoke;
        public Color32 SparkPrimary;
        public Color32 SparkGlow;
        public Color32 SparkAccent;
    }

    static PlayerPalette GetPlayerPalette(PlayerCharacter player)
    {
        try
        {
            if (player != null && player.looks != null && player.looks.lookSet != null)
            {
                int torsoId = player.looks.GetLookId(PlayerLooks.LookPart.Torso);
                int headId = player.looks.GetLookId(PlayerLooks.LookPart.Head);
                Color torsoC = player.looks.lookSet.GetColor(torsoId);
                Color headC = player.looks.lookSet.GetColor(headId);

                // Bright gleaming highlight of the player's torso color
                Color glowC = Color.Lerp(torsoC, Color.white, 0.60f);

                return new PlayerPalette
                {
                    Smoke = new Color32(
                        (byte)Mathf.Clamp(torsoC.r * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(torsoC.g * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(torsoC.b * 255f, 0f, 255f),
                        180),
                    SparkPrimary = new Color32(
                        (byte)Mathf.Clamp(torsoC.r * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(torsoC.g * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(torsoC.b * 255f, 0f, 255f),
                        255),
                    SparkGlow = new Color32(
                        (byte)Mathf.Clamp(glowC.r * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(glowC.g * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(glowC.b * 255f, 0f, 255f),
                        255),
                    SparkAccent = new Color32(
                        (byte)Mathf.Clamp(headC.r * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(headC.g * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(headC.b * 255f, 0f, 255f),
                        255)
                };
            }
        }
        catch { }

        return new PlayerPalette
        {
            Smoke = new Color32(235, 235, 235, 180),
            SparkPrimary = new Color32(255, 255, 255, 255),
            SparkGlow = new Color32(255, 240, 200, 255),
            SparkAccent = new Color32(220, 220, 255, 255)
        };
    }

    static void EmitPuffAndSparks(ParticleSystem ps, Vector3 position, PlayerPalette palette)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(true);
        ps.Play(true);

        // 1. Billowing smoke cloud matching player outfit color (lingers for 3.0s)
        for (int i = 0; i < 35; i++)
        {
            Vector3 p = position + UnityEngine.Random.insideUnitSphere * 0.45f;
            Vector3 v = UnityEngine.Random.insideUnitSphere * 0.35f + Vector3.up * 0.25f;
            float size = UnityEngine.Random.Range(1.6f, 2.6f);
            float lifetime = UnityEngine.Random.Range(2.8f, 3.2f);
            ps.Emit(p, v, size, lifetime, palette.Smoke);
        }

        // 2. High-speed flare / firework sparks bursting outward, tied to player colors
        for (int i = 0; i < 35; i++)
        {
            Vector3 p = position + UnityEngine.Random.insideUnitSphere * 0.15f;
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            float spd = UnityEngine.Random.Range(3.5f, 7.5f);
            Vector3 v = dir * spd + Vector3.up * 1.5f;
            float size = UnityEngine.Random.Range(0.12f, 0.28f);
            float lifetime = UnityEngine.Random.Range(0.35f, 0.75f);
            Color32 sparkColor = (i % 3 == 0)
                ? palette.SparkPrimary
                : (i % 3 == 1)
                    ? palette.SparkGlow
                    : palette.SparkAccent;
            ps.Emit(p, v, size, lifetime, sparkColor);
        }
    }

    static void SpawnSmoke(Vector3 position, PlayerCharacter player)
    {
        if (_spawnSmoke == null || !_spawnSmoke.Value) return;
        try
        {
            var template = GetSmokeTemplate(player);
            if (template == null) return;

            var puff = UnityEngine.Object.Instantiate(template, position, Quaternion.identity);
            if (puff != null)
            {
                puff.SetActive(true);
                puff.transform.position = position;
                puff.transform.localScale = Vector3.one * 1.8f;

                PlayerPalette palette = GetPlayerPalette(player);

                var emitters = puff.GetComponentsInChildren<ParticleSystem>(true);
                if (emitters != null && emitters.Length > 0)
                {
                    foreach (var ps in emitters)
                    {
                        EmitPuffAndSparks(ps, position, palette);
                    }
                }
                else
                {
                    var ps = puff.GetComponent<ParticleSystem>();
                    EmitPuffAndSparks(ps, position, palette);
                }

                UnityEngine.Object.Destroy(puff, 4.5f);
            }
        }
        catch (System.Exception e)
        {
            if (_diagnostics != null && _diagnostics.Value)
            {
                Plugin.Log.LogInfo($"EnderBuoy: smoke spawn error: {e.Message}");
            }
        }
    }


    static void Cancel(string reason)
    {
        Plugin.Log.LogInfo($"EnderBuoy: cancelled ({reason}).");
        _tracking = false;
        _trackedProp = null;
        _trackedTransform = null;
        _trackedRb = null;
    }
}

[HarmonyLib.HarmonyPatch(typeof(Prop), "SetDropped",
    new System.Type[] { typeof(PlayerCharacter), typeof(Vector3), typeof(Vector3) })]
static class ThrowPatch3
{
    [HarmonyLib.HarmonyPostfix]
    static void Postfix(Prop __instance, PlayerCharacter holder, Vector3 throwForce, Vector3 throwTorque)
    {
        Blink.OnThrown(__instance, holder, throwForce);
    }
}

[HarmonyLib.HarmonyPatch(typeof(Prop), "SetDropped",
    new System.Type[] { typeof(PlayerCharacter) })]
static class ThrowPatch1
{
    [HarmonyLib.HarmonyPostfix]
    static void Postfix(Prop __instance, PlayerCharacter holder)
    {
        Blink.OnDroppedWithoutForce(__instance, holder);
    }
}

[HarmonyLib.HarmonyPatch(typeof(PlatformingBody), "OnCollisionEnter")]
static class PlatformingBodyCollisionPatch
{
    [HarmonyLib.HarmonyPostfix]
    static void Postfix(PlatformingBody __instance, Collision col)
    {
        if (__instance == null || col == null) return;
        Blink.OnPropCollisionEnter(__instance.gameObject, col);
    }
}
