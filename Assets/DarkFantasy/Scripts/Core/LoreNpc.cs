using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Something that talks when you stand near it and press E: a prisoner, a ghost, a carved stone,
    /// a wooden duck. Purely local (each player reads at their own pace). Pages advance with E; walking
    /// away ends the conversation. Optional flourish when spoken to (a tune, a squeak, a bob).
    /// </summary>
    public class LoreNpc : MonoBehaviour, IInteractable
    {
        public enum Flourish : byte { None, Tune, Squeak, Whisper }

        public string speaker = "A prisoner";
        [TextArea(2, 6)] public string[] pages;
        public string promptVerb = "Talk to";
        public float interactDistance = 3f;
        public Flourish flourish = Flourish.None;
        [Tooltip("Optional: turns toward the player while talking.")]
        public Transform lookRoot;

        /// <summary>UI hooks: (speaker, text, page, pages) / conversation over.</summary>
        public static event System.Action<string, string, int, int> Talk;
        public static event System.Action Silence;
        static LoreNpc _active;

        int _page = -1;
        Quaternion _restRot;
        float _bob;

        void OnEnable() { if (!Interactables.All.Contains(this)) Interactables.All.Add(this); if (lookRoot) _restRot = lookRoot.rotation; }
        void OnDisable() { Interactables.All.Remove(this); if (_active == this) End(); }

        float Dist(PlayerController p) => Vector3.Distance(MathUtil.Flat(p.transform.position), MathUtil.Flat(transform.position));

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (p == null || !p.CanInteract || pages == null || pages.Length == 0) return false;
            if (Dist(p) > interactDistance || Mathf.Abs(p.transform.position.y - transform.position.y) > 2.5f) return false;
            prompt = _active == this ? (_page + 1 < pages.Length ? "Listen" : "Farewell") : $"{promptVerb} {speaker}";
            return true;
        }

        void Update()
        {
            var p = PlayerController.Local;
            if (_active == this && (p == null || Dist(p) > interactDistance + 1.5f)) End();
            if (GameInput.InteractPressed && LocalCanInteract(out _)) Next();
            if (lookRoot)
            {
                Quaternion want = _restRot;
                if (_active == this && p != null)
                {
                    Vector3 d = MathUtil.Flat(p.transform.position - lookRoot.position);
                    if (d.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(d);
                }
                lookRoot.rotation = Quaternion.Slerp(lookRoot.rotation, want, Time.deltaTime * 3f);
            }
            if (_bob > 0f) { _bob -= Time.deltaTime; transform.localRotation = Quaternion.Euler(Mathf.Sin(_bob * 30f) * 12f * _bob, transform.localEulerAngles.y, 0f); }
        }

        /// <summary>Development (bridge): next page, as if E was pressed.</summary>
        public void DevTalk(string _) => Next();

        void Next()
        {
            if (_active != null && _active != this) _active.End();
            _active = this;
            _page++;
            if (_page >= pages.Length) { End(); return; }
            if (_page == 0) DoFlourish();
            Talk?.Invoke(speaker, pages[_page], _page, pages.Length);
        }

        void End()
        {
            _page = -1;
            if (_active == this) { _active = null; Silence?.Invoke(); }
        }

        void DoFlourish()
        {
            Vector3 c = transform.position + Vector3.up * 1.2f;
            switch (flourish)
            {
                case Flourish.Tune:
                    Sfx.Play(SfxId.SpellAnthem, c, 0.9f, 0.85f);
                    SpellFx.NoteBurst(c, new Color(0.6f, 0.85f, 1f));
                    break;
                case Flourish.Squeak:
                    Sfx.Play(SfxId.UiClick, c, 1f, 2.4f);
                    Sfx.Play(SfxId.WaterDrip, c, 0.8f, 1.4f);
                    _bob = 1f;
                    break;
                case Flourish.Whisper:
                    Sfx.Play(SfxId.SpellCurse, c, 0.35f, 1.4f);
                    break;
            }
        }
    }
}
