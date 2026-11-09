using UnityEngine;
using UnityEngine.UI;

namespace DarkFantasy.UI
{
    /// <summary>
    /// The first screen of the game: four character slots. Each character has its own name, class
    /// and progression (SaveSlots); choosing one leads to the lobby menu, an empty slot creates one.
    /// </summary>
    public partial class GameUI
    {
        RectTransform _scrChars, _scrNewChar;
        readonly Text[] _slotTitle = new Text[SaveSlots.Max], _slotInfo = new Text[SaveSlots.Max];
        readonly Button[] _slotPlay = new Button[SaveSlots.Max], _slotDelete = new Button[SaveSlots.Max];
        InputField _newName;
        Text _newClassText, _charsStatus;
        PlayerClass _newClass;
        int _newSlot, _confirmDelete = -1;

        void BuildCharacterScreens()
        {
            // ---------------------------------------------------------- choose
            {
                _scrChars = MakeScreen("Choose your Character", new Vector2(520, 380), out var c);
                float y = -8;
                for (int i = 0; i < SaveSlots.Max; i++)
                {
                    int slot = i;
                    var row = UIFactory.Rect("Slot" + i, c, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(460, 62));
                    UIFactory.Image(row, skin.panelClean, true, new Color(0.8f, 0.75f, 0.7f));
                    _slotTitle[i] = UIFactory.Text(UIFactory.Rect("T", row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -6), new Vector2(270, 22)), "", 17, skin.bone0, TextAnchor.MiddleLeft, true);
                    _slotInfo[i] = UIFactory.Text(UIFactory.Rect("I", row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -31), new Vector2(290, 26)), "", 11, skin.bone3, TextAnchor.UpperLeft);
                    _slotPlay[i] = UIFactory.Button(row, "Play", new Vector2(1, 0.5f), new Vector2(-72, 12), 110, () => OnSlotPlay(slot));
                    _slotDelete[i] = UIFactory.Button(row, "Delete", new Vector2(1, 0.5f), new Vector2(-72, -14), 90, () => OnSlotDelete(slot), true);
                    y -= 68;
                }
                _charsStatus = UIFactory.Label(c, "", new Vector2(0.5f, 1f), new Vector2(0, y - 6), new Vector2(460, 16), 11, skin.bone3);
                UIFactory.Button(c, "Options", new Vector2(0.5f, 1f), new Vector2(-66, y - 30), 120, () => ShowOptions(true), true);
                UIFactory.Button(c, "Quit", new Vector2(0.5f, 1f), new Vector2(66, y - 30), 120, Quit, true);
            }
            // ---------------------------------------------------------- create
            {
                _scrNewChar = MakeScreen("New Character", new Vector2(360, 250), out var c);
                float y = -8;
                UIFactory.Label(c, "Name", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                _newName = UIFactory.Field(c, "", "Nameless", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196);
                _newName.characterLimit = 20;
                y -= 34;
                UIFactory.Label(c, "Class", new Vector2(0.5f, 1f), new Vector2(-104, y - 12), new Vector2(84, 22), 16, skin.bone3, TextAnchor.MiddleLeft);
                var cls = UIFactory.Button(c, "", new Vector2(0.5f, 1f), new Vector2(38, y - 12), 196, () => { _newClass = (PlayerClass)(((int)_newClass + 1) % 5); RefreshNewClass(); }, true);
                _newClassText = cls.GetComponentInChildren<Text>();
                y -= 30;
                UIFactory.Label(c, "the class is the character's for good", new Vector2(0.5f, 1f), new Vector2(0, y - 10), new Vector2(300, 16), 11, skin.bone4);
                y -= 30;
                UIFactory.Button(c, "Create", new Vector2(0.5f, 1f), new Vector2(-70, y - 14), 130, OnCreateCharacter);
                UIFactory.Button(c, "Back", new Vector2(0.5f, 1f), new Vector2(70, y - 14), 130, () => ShowScreen(_scrChars), true);
            }
            SaveSlots.Changed += RefreshSlots;
            RefreshSlots();
        }

        void RefreshSlots()
        {
            for (int i = 0; i < SaveSlots.Max; i++)
            {
                if (_slotTitle[i] == null) continue;
                bool ex = SaveSlots.Exists(i);
                _slotTitle[i].text = ex ? $"{SaveSlots.Name(i)}  ·  {SaveSlots.ClassName(SaveSlots.Class(i))}" : "Empty slot";
                string flame = StartNames[Mathf.Clamp(SaveSlots.Flame(i), 0, StartNames.Length - 1)];
                _slotInfo[i].text = ex ? $"{flame}  ·  {SaveSlots.BossSets(i)} boss armour(s)  ·  {SaveSlots.Souls(i)} souls  ·  {SaveSlots.SkillRanks(i)} skill ranks" : "a new soul waits here";
                _slotPlay[i].GetComponentInChildren<Text>().text = ex ? (SaveSlots.Current == i ? "Play ✓" : "Play") : "Create";
                _slotDelete[i].gameObject.SetActive(ex);
                _slotDelete[i].GetComponentInChildren<Text>().text = _confirmDelete == i ? "Sure?" : "Delete";
            }
        }

        void OnSlotPlay(int i)
        {
            _confirmDelete = -1;
            if (!SaveSlots.Exists(i))
            {
                _newSlot = i; _newClass = PlayerClass.Knight;
                if (_newName) _newName.text = "";
                RefreshNewClass();
                ShowScreen(_scrNewChar);
                return;
            }
            SaveSlots.Select(i);
            EnterWithCharacter();
        }

        void OnSlotDelete(int i)
        {
            if (_confirmDelete != i) { _confirmDelete = i; RefreshSlots(); if (_charsStatus) _charsStatus.text = "Click again to erase this character and all its progress."; return; }
            _confirmDelete = -1;
            SaveSlots.Delete(i);
            if (_charsStatus) _charsStatus.text = "Character erased.";
            RefreshSlots();
        }

        void OnCreateCharacter()
        {
            string n = _newName ? _newName.text : "";
            SaveSlots.Create(_newSlot, n, _newClass);
            EnterWithCharacter();
        }

        /// <summary>The chosen character's name and class fill the lobby menu.</summary>
        void EnterWithCharacter()
        {
            if (_nameField) _nameField.text = MenuSettings.PlayerName;
            RefreshClass();
            RefreshStart();
            RefreshSlots();
            ShowScreen(_scrMain);
            if (_statusText) _statusText.text = $"Playing as {MenuSettings.PlayerName}. Create a lobby, or find your friends' one.";
        }

        void RefreshNewClass() { if (_newClassText) _newClassText.text = ClassLabel(_newClass); }
    }
}
