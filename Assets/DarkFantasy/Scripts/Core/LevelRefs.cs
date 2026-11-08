using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Riferimenti del livello (punti di spawn, arena del boss). Uno per scena.</summary>
    public class LevelRefs : MonoBehaviour
    {
        public static LevelRefs Instance { get; private set; }

        [Header("Giocatori")]
        public Transform playerSpawn;         // il falò
        public float spawnSpread = 1.6f;

        [Header("Nemici minori")]
        public Transform[] enemySpawns;

        [Header("Boss")]
        public Transform bossSpawn;
        public Transform arenaRoot;            // posizione del cancello di nebbia, forward = verso l'interno
        public Vector3 arenaCenter = new Vector3(0, 0, 45);
        public Vector3 arenaSize = new Vector3(26, 16, 38);

        [System.Serializable]
        public class CheckpointDef
        {
            public string name;
            public Transform point;
            [Tooltip("A living player within this distance unlocks the checkpoint (0 = only by script).")]
            public float reachRadius = 6f;
        }

        [System.Serializable]
        public class AtmosphereZone
        {
            public string name;
            public Bounds bounds;
            public Color fog = new Color(0.05f, 0.05f, 0.06f);
            public float fogDensity = 0.05f;
            public Color ambient = new Color(0.14f, 0.13f, 0.12f);
            public float exposure = 0.5f;
            [Tooltip("Show the sunset sky dome (open-air zones).")]
            public bool sky;
            [Tooltip("Re-light the main directional light (sun) inside this zone.")]
            public bool overrideSun;
            public Color sunColor = new Color(1f, 0.55f, 0.3f);
            public float sunIntensity = 1.5f;
            public Vector3 sunEuler = new Vector3(10f, 180f, 0f);
        }

        [Tooltip("Sunset dome shown in zones with 'sky' (the terrace).")]
        public SkyDome skyDome;
        [Tooltip("The scene's main directional light (moon outside, sunset on the terrace).")]
        public Light mainLight;

        [Header("Castello")]
        [Tooltip("Respawn points in order of progress: 0 = the bonfire (playerSpawn).")]
        public CheckpointDef[] checkpoints;
        [Tooltip("Zones with their own fog/ambient (castle interior, terrace). The throne room uses the arena bounds.")]
        public AtmosphereZone[] zones;
        public Bounds dragonArena = new Bounds(new Vector3(0, 10, 172), new Vector3(46, 24, 50));

        public bool IsInsideDragonArena(Vector3 p) => dragonArena.Contains(p);

        [Header("Castle enemies")]
        public Transform[] castleKnights;      // Rotting Gaolers
        public Transform[] castleArchers;      // Hollow Bowmen
        public Transform[] wardens;            // the key's keepers (bigger knights)
        public Transform dragonSpawn;
        public Transform dragonGate;           // fog gate at the top of the stairs, forward = onto the terrace

        [Header("Beyond the bridge")]
        public Transform[] shardWraiths;
        public Transform[] glassRevenants;
        public Transform mirrorSpawn;
        public Transform mirrorGate;           // fog gate of the Glass Temple, forward = inside
        public Bounds mirrorArena = new Bounds(new Vector3(0, -20, 505), new Vector3(52, 30, 52));
        public bool IsInsideMirrorArena(Vector3 p) => mirrorArena.Contains(p);
        [Tooltip("Where the radiant chest rises if the dice want it (the forgotten vault).")]
        public Transform vaultChest;

        [Header("Atmosfera")]
        public Color outdoorFog = new Color(0.4f, 0.42f, 0.38f);
        public float outdoorFogDensity = 0.034f;
        public Color indoorFog = new Color(0.25f, 0.22f, 0.19f);
        public float indoorFogDensity = 0.011f;
        [Tooltip("Ambient light inside the throne room (readable fight, warm fire-lit tone).")]
        public Color indoorAmbient = new Color(0.66f, 0.58f, 0.5f);
        [Tooltip("Extra exposure (EV) inside the throne room.")]
        public float indoorExposure = 0.8f;

        void Awake() => Instance = this;

        public bool IsInsideArena(Vector3 p)
        {
            var b = new Bounds(arenaCenter, arenaSize);
            return b.Contains(p);
        }

        public Vector3 GetPlayerSpawn(ulong clientId, out Quaternion rot)
        {
            Transform sp = playerSpawn;
            var prog = CastleProgress.Instance;
            if (prog != null && prog.IsSpawned && checkpoints != null && prog.Checkpoint > 0 && prog.Checkpoint < checkpoints.Length && checkpoints[prog.Checkpoint].point)
                sp = checkpoints[prog.Checkpoint].point;
            rot = sp ? sp.rotation : Quaternion.identity;
            Vector3 c = sp ? sp.position : Vector3.zero;
            float a = (clientId % 8) * 45f * Mathf.Deg2Rad;
            Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * spawnSpread;
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var hit, 30f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                p = hit.point + Vector3.up * 0.05f;
            return p;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.3f);
            Gizmos.DrawWireCube(arenaCenter, arenaSize);
        }
    }

}
