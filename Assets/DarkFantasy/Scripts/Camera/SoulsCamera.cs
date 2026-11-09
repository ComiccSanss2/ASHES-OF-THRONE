using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Camera in terza persona stile Souls, scritta a mano (nessuna dipendenza da Cinemachine).
    ///
    /// - Modalità libera: orbita 360° con mouse / levetta destra, follow con smoothing regolabile.
    /// - Modalità lock-on: tasto dedicato (rotellina / Tab / R3). La camera si mette dietro al
    ///   giocatore lungo la linea giocatore→nemico e inquadra entrambi. Flick a destra/sinistra
    ///   del mouse o della levetta cambia bersaglio.
    /// - Collisioni: SphereCast dal pivot alla posizione desiderata; la distanza si accorcia
    ///   rapidamente quando c'è un ostacolo e torna morbida quando lo spazio si libera.
    /// - Senza giocatore (menu principale) fa una lenta orbita cinematografica.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class SoulsCamera : MonoBehaviour
    {
        public static SoulsCamera Instance { get; private set; }

        [Header("Follow")]
        public Transform followTarget;
        public float pivotHeight = 1.55f;
        public float shoulderOffset = 0.0f;
        public float distance = 4.3f;
        [Tooltip("Morbidezza dell'inseguimento della posizione (più alto = più rigido).")]
        public float followSharpness = 12f;
        [Tooltip("Morbidezza della rotazione in modalità libera.")]
        public float rotationSharpness = 25f;
        public float minPitch = -40f;
        public float maxPitch = 70f;

        [Header("Lock-on")]
        public float lockRange = 22f;
        public float lockBreakRange = 28f;
        public float lockRotationSharpness = 9f;
        public float lockPitchOffset = 8f;
        public float switchThreshold = 0.7f;
        public float switchCooldown = 0.35f;

        [Header("Collisioni")]
        public float collisionRadius = 0.28f;
        public float minDistance = 0.6f;
        public float collisionInSharpness = 30f;
        public float collisionOutSharpness = 4f;

        [Header("Aim (archer, hold right mouse)")]
        public float aimDistance = 1.9f;
        public float aimShoulder = 0.62f;
        public float aimPivotDrop = 0.12f;
        public float aimFovDelta = -11f;
        [Tooltip("Look sensitivity while aiming (finer).")]
        public float aimLookScale = 0.55f;

        [Header("Menu")]
        public Vector3 menuOrbitCenter = new Vector3(0, 4, 0);
        public float menuOrbitRadius = 22f;
        public float menuOrbitSpeed = 4f;

        public Camera Cam { get; private set; }
        public LockOnTarget CurrentTarget { get; private set; }
        public bool IsLocked => CurrentTarget != null;

        /// <summary>Avanti/destra della camera sul piano XZ (usati dal movimento del giocatore).</summary>
        public Vector3 PlanarForward => MathUtil.Flat(transform.forward).normalized;
        public Vector3 PlanarRight => MathUtil.Flat(transform.right).normalized;

        float _yaw, _pitch = 12f;
        float _currentDistance;
        Vector3 _smoothedPivot;
        float _switchCooldownTimer;
        float _lostSightTimer;
        float _menuAngle;
        bool _snapNextFrame = true;

        void Awake()
        {
            Instance = this;
            Cam = GetComponent<Camera>();
            _currentDistance = distance;
            if (Cam) _baseFov = Cam.fieldOfView;
        }

        float _aim, _aimUntil = -1f, _baseFov = 60f;
        /// <summary>0..1: how far the over-the-shoulder aim view is blended in.</summary>
        public float AimAmount => _aim;
        /// <summary>Called every frame by the local archer while aiming (released automatically).</summary>
        public static void RequestAim() { if (Instance) Instance._aimUntil = Time.time + 0.1f; }
        /// <summary>Ray through the centre of the screen (the crosshair).</summary>
        public Ray AimRay => Cam ? Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position, transform.forward);

        float _shake, _shakeTime;

        /// <summary>Screen shake (impacts, slams). Amplitude in metres, decays quickly.</summary>
        public static void Shake(float amplitude, float duration = 0.18f)
        {
            if (Instance == null) return;
            amplitude *= GameSettings.ScreenShake;
            if (amplitude <= Instance._shake * Mathf.Clamp01(Instance._shakeTime / Mathf.Max(0.01f, duration))) return;
            Instance._shake = amplitude; Instance._shakeTime = duration; Instance._shakeDur = duration;
        }
        float _shakeDur = 0.18f;
        Quaternion _baseRot = Quaternion.identity;

        public void SetFollowTarget(Transform t)
        {
            followTarget = t;
            CurrentTarget = null;
            _snapNextFrame = true;
            if (t != null) _yaw = t.eulerAngles.y;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (followTarget == null) { MenuOrbit(dt); return; }

            _aim = Mathf.MoveTowards(_aim, Time.time < _aimUntil && !IsLocked ? 1f : 0f, dt * 6f);
            float aimE = _aim * _aim * (3f - 2f * _aim);
            if (Cam) Cam.fieldOfView = _baseFov + aimFovDelta * aimE;
            Vector3 rawPivot = followTarget.position + Vector3.up * (pivotHeight - aimPivotDrop * aimE);
            if (_snapNextFrame) { _smoothedPivot = rawPivot; }
            _smoothedPivot = Vector3.Lerp(_smoothedPivot, rawPivot, MathUtil.Damp(followSharpness, dt));

            if (GameInput.LockOnPressed) ToggleLock();
            ValidateLock(dt);

            Quaternion desiredRot;
            if (IsLocked)
            {
                HandleTargetSwitch(dt);
                // Direzione dal giocatore al nemico: la camera sta dietro al giocatore su questa linea.
                Vector3 targetPoint = CurrentTarget.Point;
                Vector3 toTarget = targetPoint - _smoothedPivot;
                Vector3 flat = MathUtil.Flat(toTarget);
                if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
                float targetYaw = Quaternion.LookRotation(flat).eulerAngles.y;
                // Inclinazione: guarda leggermente verso il basso + compensa l'altezza del bersaglio
                float horiz = flat.magnitude;
                float heightPitch = -Mathf.Atan2(toTarget.y, Mathf.Max(horiz, 0.1f)) * Mathf.Rad2Deg * 0.6f;
                float targetPitch = Mathf.Clamp(lockPitchOffset + heightPitch + 6f * CurrentTarget.framingSize, minPitch, maxPitch);

                float k = MathUtil.Damp(lockRotationSharpness, dt);
                _yaw = Mathf.LerpAngle(_yaw, targetYaw, k);
                _pitch = Mathf.Lerp(_pitch, targetPitch, k);
                desiredRot = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                Vector2 look = GameInput.Look * Mathf.Lerp(1f, aimLookScale, aimE);
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
                desiredRot = Quaternion.Euler(_pitch, _yaw, 0f);
                if (!_snapNextFrame)
                    desiredRot = Quaternion.Slerp(_baseRot, desiredRot, MathUtil.Damp(rotationSharpness, dt));
            }

            // big bosses: the camera backs off so the whole body (head, tail, claws) stays readable up close
            float wantedDistance = distance * (IsLocked ? Mathf.Lerp(1f, 1.75f, Mathf.Clamp01((CurrentTarget.framingSize - 1f) / 2f)) : 1f);
            wantedDistance = Mathf.Lerp(wantedDistance, aimDistance, aimE);
            Vector3 pivot = _smoothedPivot + desiredRot * Vector3.right * Mathf.Lerp(shoulderOffset, aimShoulder, aimE);
            Vector3 back = desiredRot * Vector3.back;

            // ---- collisione: spherecast dal pivot all'indietro
            float allowed = wantedDistance;
            if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, wantedDistance, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(minDistance, hit.distance - 0.05f);
            float sharp = allowed < _currentDistance ? collisionInSharpness : collisionOutSharpness;
            _currentDistance = _snapNextFrame ? allowed : Mathf.Lerp(_currentDistance, allowed, MathUtil.Damp(sharp, dt));
            // non attraversare mai: se la distanza attuale supera quella permessa la tagliamo
            if (_currentDistance > allowed + 0.4f) _currentDistance = allowed + 0.4f;

            Vector3 camPos = pivot + back * _currentDistance;
            Quaternion camRot = desiredRot;
            _baseRot = desiredRot;
            if (_shakeTime > 0f)
            {
                _shakeTime -= dt;
                float k = Mathf.Clamp01(_shakeTime / _shakeDur); k *= k;
                float tt = Time.time * 47f;
                Vector3 off = new Vector3(Mathf.PerlinNoise(tt, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tt) - 0.5f, 0f) * 2f * _shake * k;
                camPos += desiredRot * off;
                camRot = desiredRot * Quaternion.Euler(off.y * 6f, off.x * 6f, (Mathf.PerlinNoise(tt, tt) - 0.5f) * _shake * k * 18f);
            }
            transform.SetPositionAndRotation(camPos, camRot);
            _snapNextFrame = false;
        }

        // ------------------------------------------------------------------ lock-on
        void ToggleLock()
        {
            if (IsLocked) { CurrentTarget = null; return; }
            CurrentTarget = FindBestTarget(null, 0);
            if (CurrentTarget == null)
            {
                // Nessun bersaglio: ricentra la camera dietro al personaggio (come nei Souls)
                _yaw = followTarget.eulerAngles.y;
                _pitch = 12f;
            }
            _lostSightTimer = 0f;
        }

        void ValidateLock(float dt)
        {
            if (!IsLocked) return;
            if (!CurrentTarget || !CurrentTarget.IsValid) { CurrentTarget = FindBestTarget(null, 0); return; }
            float d = Vector3.Distance(followTarget.position, CurrentTarget.transform.position);
            if (d > lockBreakRange) { CurrentTarget = null; return; }
            if (!HasLineOfSight(CurrentTarget)) _lostSightTimer += dt; else _lostSightTimer = 0f;
            if (_lostSightTimer > 2f) CurrentTarget = null;
        }

        void HandleTargetSwitch(float dt)
        {
            _switchCooldownTimer -= dt;
            float axis = GameInput.TargetSwitchAxis;
            if (_switchCooldownTimer > 0f || Mathf.Abs(axis) < switchThreshold) return;
            var next = FindBestTarget(CurrentTarget, axis > 0 ? 1 : -1);
            if (next != null) { CurrentTarget = next; _switchCooldownTimer = switchCooldown; }
        }

        /// <summary>
        /// side = 0: bersaglio più vicino al centro dello schermo.
        /// side = ±1: bersaglio più vicino nello schermo dal lato indicato rispetto a "current".
        /// </summary>
        LockOnTarget FindBestTarget(LockOnTarget current, int side)
        {
            if (followTarget == null || Cam == null) return null;
            LockOnTarget best = null;
            float bestScore = float.MaxValue;
            float currentX = current != null ? Cam.WorldToViewportPoint(current.Point).x : 0.5f;

            foreach (var t in LockOnTarget.All)
            {
                if (t == null || t == current || !t.IsValid) continue;
                float dist = Vector3.Distance(followTarget.position, t.transform.position);
                if (dist > lockRange) continue;
                Vector3 vp = Cam.WorldToViewportPoint(t.Point);
                if (vp.z <= 0f) continue;
                if (side == 0 && (vp.x < -0.1f || vp.x > 1.1f)) continue;
                if (!HasLineOfSight(t)) continue;

                float score;
                if (side == 0)
                {
                    score = Mathf.Abs(vp.x - 0.5f) * 3f + Mathf.Abs(vp.y - 0.5f) + dist / lockRange;
                }
                else
                {
                    float dx = (vp.x - currentX) * side;
                    if (dx <= 0.01f) continue;
                    score = dx + dist / lockRange * 0.3f;
                }
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        bool HasLineOfSight(LockOnTarget t)
        {
            Vector3 from = followTarget.position + Vector3.up * pivotHeight;
            Vector3 to = t.Point;
            return !Physics.Linecast(from, to, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        public void ClearLock() => CurrentTarget = null;

        // ------------------------------------------------------------------ menu
        void MenuOrbit(float dt)
        {
            _menuAngle += menuOrbitSpeed * dt;
            float a = _menuAngle * Mathf.Deg2Rad;
            Vector3 pos = menuOrbitCenter + new Vector3(Mathf.Sin(a), 0.35f, Mathf.Cos(a)) * menuOrbitRadius;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(menuOrbitCenter - pos + Vector3.down * 2f);
            _snapNextFrame = true;
        }
    }
}
