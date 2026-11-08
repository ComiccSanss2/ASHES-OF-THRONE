using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkFantasy.Procedural;
using DarkFantasy.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Costruttore "one click" del progetto: menu  Dark Fantasy ▸ Costruisci Tutto.
    ///  1. layer, impostazioni player, import degli sprite UI (9-slice dal manifest del kit)
    ///  2. texture PS2 generate, materiali URP, mesh procedurali (rovine, castello, armi)
    ///  3. Animator Controller dal rig "Solus" (+ livello UpperBody per la parata)
    ///  4. prefab di rete: Giocatore, Guardiano, Boss, Arena (cancello di nebbia), Onda d'urto
    ///  5. scena "DarkFantasy" completa (zona esterna, sala del trono, luci, post-processing, UI, rete)
    /// Può essere rieseguito in qualsiasi momento: rigenera tutto in Assets/DarkFantasy/Generated.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string Root = "Assets/DarkFantasy";
        const string Gen = Root + "/Generated";
        const string Kit = "Assets/gothic-manor-pixel-ui-kit-ravenhollow-lite-v1.0";
        // knight + greatsword animation set authored in Blender (Tools/gs_anim.py), same Solus rig/mesh
        const string KnightFbx = AnimationAnalysis.KnightFbx;
        const string KnightTex = "Assets/Low Poly Knights/Textures/Solus/Vension 2/Solus_Knight_Base_Color.png";
        // archer class: same Solus rig, hooded mesh + war-bow + BW_* clips (Tools/archer_anim.py)
        const string ArcherFbx = "Assets/DarkFantasy/Art/Archer.fbx";
        const string ArcherTex = "Assets/DarkFantasy/Art/Archer_Base_Color.png";
        public const string ScenePath = Root + "/Scenes/DarkFantasy.unity";
        const string Title = "Dark Fantasy";

        // risorse generate
        static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        [MenuItem("Dark Fantasy/Build Everything (playable scene)", priority = 0)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[DF] Esci dal Play Mode prima di costruire."); return; }
            try
            {
                Step("Project settings", 0.02f);
                EnsureFolders();
                EnsureLayer(Layers.CharacterName, 8);
                ConfigurePlayerSettings();

                Step("Ravenhollow UI sprites", 0.08f);
                var skin = BuildUISkin();

                Step("PS2 textures", 0.18f);
                BuildTexturesAndMaterials();

                Step("Procedural meshes", 0.35f);
                BuildMeshes();

                Step("Animation import + analysis (swing frames, sword socket)", 0.45f);
                ConfigureKnightImport();
                AnalyzeAnimations();
                ConfigureArcherImport();
                ConfigureDragonImport();
                ConfigureBardImport();

                Step("Animator", 0.5f);
                var ctrl = BuildAnimator();
                var archerCtrl = BuildArcherAnimator();

                Step("Prefabs", 0.6f);
                var prefabs = BuildPrefabs(ctrl, archerCtrl);

                Step("Audio", 0.7f);
                ConfigureAudio();

                Step("Scene", 0.75f);
                BuildScene(skin, prefabs);

                AssetDatabase.SaveAssets();
                Debug.Log("[DF] Build complete ✓  Press Play, then Create Lobby → Begin the Journey.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DF] Build failed: " + e);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Dark Fantasy/Open Game Scene", priority = 1)]
        public static void OpenScene()
        {
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Debug.LogWarning("[DF] Scene not generated yet: run 'Build Everything'.");
        }

        [MenuItem("Dark Fantasy/Build Windows Player (multiplayer test)", priority = 20)]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) BuildAll();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/DarkFantasy.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[DF] Build: {report.summary.result} → {Path.GetFullPath(opts.locationPathName)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(opts.locationPathName);
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        }

        static void Step(string s, float p)
        {
            EditorUtility.DisplayProgressBar(Title, s, p);
            Debug.Log("[DF] " + s + "…");
        }

        // ================================================================== impostazioni
        static void EnsureFolders()
        {
            foreach (var f in new[] { Root + "/Scenes", Gen, Gen + "/Textures", Gen + "/Materials", Gen + "/Meshes", Gen + "/Prefabs", Gen + "/Animation", Gen + "/UI", Gen + "/Rendering" })
                EnsureFolder(f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void EnsureLayer(string name, int preferred)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return;
            for (int i = preferred; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[DF] Creato layer '{name}' (#{i})");
                    return;
                }
            }
            Debug.LogWarning("[DF] Nessuno slot libero per il layer " + name);
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "Ashes of the Throne";
            PlayerSettings.companyName = "DarkFantasy";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = false;
        }

        // ================================================================== UI
        static Sprite UISprite(string rel, int l = 0, int t = 0, int r = 0, int b = 0)
        {
            string path = $"{Kit}/1x/{rel}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning("[DF] Sprite mancante: " + path); return null; }
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.spritePixelsPerUnit = 100;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteBorder = new Vector4(l, b, r, t); // Unity: x=sinistra, y=basso, z=destra, w=alto
            imp.SetTextureSettings(s);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static UISkin BuildUISkin()
        {
            string path = Gen + "/UI/RavenhollowSkin.asset";
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(path);
            if (skin == null) { skin = ScriptableObject.CreateInstance<UISkin>(); AssetDatabase.CreateAsset(skin, path); }

            // inset l/t/r/b dalla tabella di LLM_GUIDE.md / manifest.json del kit
            skin.windowFleur = UISprite("containers/window_fleur", 40, 60, 42, 42);
            skin.panelClean = UISprite("containers/panel_clean", 12, 12, 12, 12);
            skin.speechOrnate = UISprite("containers/dlg_speech_ornate", 16, 16, 18, 18);
            skin.nameplate = UISprite("containers/dlg_nameplate_ornate", 12, 0, 12, 0);
            skin.ribbon = UISprite("chrome/ribbon_velvet", 16, 0, 16, 0);

            skin.buttonNormal = UISprite("slice/button_primary_normal", 11, 0, 11, 0);
            skin.buttonHover = UISprite("slice/button_primary_hover", 11, 0, 11, 0);
            skin.buttonPressed = UISprite("slice/button_primary_pressed", 11, 0, 11, 0);
            skin.buttonSelected = UISprite("slice/button_primary_focus", 11, 0, 11, 0);
            skin.buttonDisabled = UISprite("slice/button_primary_disabled", 11, 0, 11, 0);
            skin.button2Normal = UISprite("slice/button_secondary_normal", 11, 0, 11, 0);
            skin.button2Hover = UISprite("slice/button_secondary_hover", 11, 0, 11, 0);
            skin.button2Pressed = UISprite("slice/button_secondary_pressed", 11, 0, 11, 0);
            skin.button2Selected = UISprite("slice/button_secondary_focus", 11, 0, 11, 0);
            skin.button2Disabled = UISprite("slice/button_secondary_disabled", 11, 0, 11, 0);

            skin.fieldIdle = UISprite("slice/field_idle", 4, 4, 4, 4);
            skin.fieldHover = UISprite("slice/field_hover", 4, 4, 4, 4);
            skin.fieldFocus = UISprite("slice/field_focus", 4, 4, 4, 4);
            skin.fieldDisabled = UISprite("slice/field_disabled", 4, 4, 4, 4);
            skin.fieldCaret = UISprite("slice/field_caret");
            skin.sliderTrack = UISprite("inputs/slider_track_normal", 4, 0, 4, 0);
            skin.sliderFill = UISprite("inputs/slider_fill_normal", 0, 0, 1, 0);
            skin.sliderKnob = UISprite("inputs/slider_knob_round_normal");
            skin.sliderKnobHover = UISprite("inputs/slider_knob_round_hover");
            skin.sliderKnobPressed = UISprite("inputs/slider_knob_round_pressed");
            skin.checkboxOn = UISprite("slice/checkbox_on_idle");
            skin.checkboxOff = UISprite("slice/checkbox_off_idle");
            skin.checkboxOnHover = UISprite("slice/checkbox_on_hover");
            skin.checkboxOffHover = UISprite("slice/checkbox_off_hover");
            skin.keycap = UISprite("inputs/keycap_normal", 8, 0, 8, 0);

            skin.barFrameHp = UISprite("slice/bar_frame_hp", 24, 0, 16, 0);
            skin.barFillHp = UISprite("slice/bar_fill_hp", 0, 0, 1, 0);
            skin.barFrameMana = UISprite("slice/bar_frame_mana", 24, 0, 16, 0);
            skin.barFillMana = UISprite("slice/bar_fill_mana", 0, 0, 1, 0);
            skin.barFrameXp = UISprite("slice/bar_frame_xp", 24, 0, 16, 0);
            skin.barFillXp = UISprite("slice/bar_fill_xp", 0, 0, 1, 0);
            skin.barTicks = UISprite("slice/bar_ticks");

            skin.iconHeart = UISprite("icons/icon_heart_24");
            skin.iconPotion = UISprite("icons/icon_potion_24");
            skin.iconSkull = UISprite("icons/icon_skull_24");
            skin.iconShield = UISprite("icons/icon_shield_24");
            skin.iconSword = UISprite("icons/icon_sword_24");
            skin.iconGear = UISprite("icons/icon_gear_24");
            skin.iconCandle = UISprite("icons/icon_candle_24");
            skin.iconCharacter = UISprite("icons/icon_character_24");
            skin.iconInfo = UISprite("icons/icon_info_24");
            skin.iconClose = UISprite("icons/icon_close_24");

            // cursore (2x = 32 px), hotspot del manifest (0,0)
            string curPath = $"{Kit}/2x/chrome/cursor_pointer_16_idle.png";
            if (AssetImporter.GetAtPath(curPath) is TextureImporter ci)
            {
                ci.textureType = TextureImporterType.Cursor;
                ci.filterMode = FilterMode.Point;
                ci.mipmapEnabled = false;
                ci.textureCompression = TextureImporterCompression.Uncompressed;
                ci.isReadable = true;
                ci.SaveAndReimport();
                skin.cursor = AssetDatabase.LoadAssetAtPath<Texture2D>(curPath);
                skin.cursorHotspot = Vector2.zero;
            }

            skin.headingFont = AssetDatabase.LoadAssetAtPath<Font>($"{Kit}/fonts/originals/jacquard12/Jacquard12-Regular.ttf");
            skin.bodyFont = AssetDatabase.LoadAssetAtPath<Font>($"{Kit}/fonts/originals/jersey10/Jersey10-Regular.ttf");
            EditorUtility.SetDirty(skin);
            return skin;
        }

        // ================================================================== texture e materiali
        static Shader LitShader => Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit");

        static Material SaveMat(string name, Material m)
        {
            string path = $"{Gen}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.renderQueue = m.renderQueue;
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                M[name] = existing;
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            M[name] = m;
            return m;
        }

        static Material LitMat(string name, Texture tex, Color color, bool cutout = false, bool doubleSided = false, Color? emission = null)
        {
            var m = new Material(LitShader);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", color);
            m.SetColor("_SpecColor", new Color(0.05f, 0.05f, 0.05f));
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (doubleSided) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
            // emissione sempre attiva (serve al "flash" quando si viene colpiti)
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission ?? Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return SaveMat(name, m);
        }

        static Material TransparentMat(string name, Texture tex, Color color, bool additive, string shaderName = "Universal Render Pipeline/Unlit")
        {
            var sh = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", additive ? 1f : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return SaveMat(name, m);
        }

        static void ConfigureKnightTexture()
        {
            // stile PS2: texture del cavaliere ridotta a 256 px
            foreach (var p in new[] { KnightTex })
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    ti.maxTextureSize = 256;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
        }

        static void BuildTexturesAndMaterials()
        {
            string T(string n) => $"{Gen}/Textures/{n}.png";
            var ground = TextureGen.Ground(T("ground_mud"));
            var bricks = TextureGen.StoneBricks(T("stone_bricks"), 1, 0.55f);
            var bricksDark = TextureGen.StoneBricks(T("stone_bricks_castle"), 7, 0.25f);
            var flag = TextureGen.Flagstones(T("flagstones"));
            var bark = TextureGen.Bark(T("bark"));
            var wood = TextureGen.RottenWood(T("rotten_wood"));
            var metal = TextureGen.RustMetal(T("rust_metal"));
            var steel = TextureGen.KnightSteel(T("steel"));
            var banner = TextureGen.Banner(T("banner"));
            var glass = TextureGen.StainedGlass(T("stained_glass"));
            var grass = TextureGen.GrassTuft(T("grass_tuft"));
            var fog = TextureGen.Fog(T("fog"));
            var dot = TextureGen.SoftDot(T("soft_dot"));
            var ash = TextureGen.Ash(T("ash"));
            var ring = TextureGen.Ring(T("shock_ring"));

            LitMat("Ground", ground, Color.white);
            LitMat("StoneRuins", bricks, Color.white);
            LitMat("StoneCastle", bricksDark, new Color(0.85f, 0.83f, 0.8f));
            LitMat("Flagstones", flag, Color.white);
            LitMat("Bark", bark, Color.white);
            LitMat("RottenWood", wood, Color.white);
            LitMat("RustMetal", metal, Color.white);
            LitMat("Steel", steel, new Color(0.85f, 0.85f, 0.85f));
            LitMat("Banner", banner, Color.white, cutout: true, doubleSided: true);
            LitMat("StainedGlass", glass, Color.white, cutout: true, doubleSided: true, emission: new Color(0.16f, 0.14f, 0.13f));
            LitMat("GrassTuft", grass, Color.white, cutout: true, doubleSided: true);
            LitMat("Ash", ash, Color.white, emission: new Color(0.05f, 0.02f, 0.01f));
            TransparentMat("FogGate", fog, new Color(0.78f, 0.76f, 0.7f, 0.85f), false);
            TransparentMat("ShockRing", ring, new Color(0.55f, 0.45f, 0.35f, 0.8f), false);
            TransparentMat("ParticleAdd", dot, Color.white, true, "Universal Render Pipeline/Particles/Unlit");
            TransparentMat("ParticleAlpha", dot, Color.white, false, "Universal Render Pipeline/Particles/Unlit");
            var trailTex = TextureGen.Trail(T("slash_trail"));
            TransparentMat("SlashTrail", trailTex, Color.white, true, "Universal Render Pipeline/Particles/Unlit");

            ConfigureKnightTexture();
            var knight = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightTex);
            LitMat("KnightPlayer", knight, new Color(0.78f, 0.76f, 0.72f));
            LitMat("KnightGuardian", knight, new Color(0.38f, 0.4f, 0.31f));
            LitMat("KnightBoss", knight, new Color(0.5f, 0.38f, 0.34f), emission: new Color(0.05f, 0.02f, 0.012f));

            // archer: one texture for body, hood, cloak, quiver, bow and arrows (swatches painted by Tools/archer_texture.py)
            if (AssetImporter.GetAtPath(ArcherTex) is TextureImporter ati)
            {
                ati.maxTextureSize = 512;   // PS2 look, but the small swatches of the bow/arrows must survive
                ati.filterMode = FilterMode.Bilinear;
                ati.textureCompression = TextureImporterCompression.Uncompressed;
                ati.SaveAndReimport();
            }
            var archerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArcherTex);
            if (archerTex == null) Debug.LogWarning("[DF] Missing " + ArcherTex);
            LitMat("ArcherPlayer", archerTex, new Color(0.84f, 0.82f, 0.78f));
            var str = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            str.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.5f));
            SaveMat("BowString", str);

            // relics (Tools/relic_models.py): blackened steel, bright edges, gold, glowing embers / runes
            LitMat("RelicDark", steel, new Color(0.2f, 0.19f, 0.19f));
            LitMat("RelicEdge", steel, new Color(1f, 0.98f, 0.94f), emission: new Color(0.05f, 0.05f, 0.05f));
            LitMat("RelicGold", metal, new Color(1f, 0.76f, 0.34f), emission: new Color(0.08f, 0.05f, 0.01f));
            LitMat("RelicEmber", null, new Color(1f, 0.45f, 0.15f), emission: new Color(2.4f, 0.75f, 0.15f));
            LitMat("RelicLeather", wood, new Color(0.42f, 0.26f, 0.16f));
            LitMat("RelicDarkwood", bark, new Color(0.62f, 0.45f, 0.36f));
            LitMat("RelicBone", ash, new Color(1f, 0.95f, 0.82f), emission: new Color(0.04f, 0.04f, 0.03f));
            LitMat("RelicRune", null, new Color(0.6f, 0.9f, 1f), emission: new Color(0.55f, 1.3f, 1.9f));
            LitMat("RelicGlow", null, new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.45f));
            LitMat("RelicCrimson", steel, new Color(0.62f, 0.07f, 0.05f), emission: new Color(0.12f, 0.01f, 0.0f));

            var vhs = new Material(Shader.Find("DarkFantasy/VHS_PS2_Screen"));
            SaveMat("VHS_Screen", vhs);
            BuildCastleMaterials();
            BuildBardMaterials();
            BuildMirrorMaterials();
        }

        // ================================================================== mesh
        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{Gen}/Meshes/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            Meshes[name] = mesh;
            return mesh;
        }

        static void BuildMeshes()
        {
            SaveMesh(ProceduralMeshes.Terrain(-85f, 85f, -112f, 96f, 1.6f), "Terrain");
            for (int i = 0; i < 6; i++) SaveMesh(ProceduralMeshes.Rock(i + 1, 1f, 0.55f + (i % 3) * 0.15f), "Rock" + i);
            for (int i = 0; i < 4; i++) SaveMesh(ProceduralMeshes.RuinedWall(i + 3, 6f + i * 1.5f, 3.2f + (i % 2) * 1.2f, 0.9f, 0.55f + 0.1f * i), "RuinWall" + i);
            SaveMesh(ProceduralMeshes.Pillar(11, 0.55f, 5.5f, true), "PillarBroken0");
            SaveMesh(ProceduralMeshes.Pillar(12, 0.55f, 5.5f, true), "PillarBroken1");
            SaveMesh(ProceduralMeshes.Pillar(13, 0.55f, 5.5f, false), "PillarRuin");
            SaveMesh(ProceduralMeshes.Pillar(14, 0.85f, 13f, false), "PillarHall");
            SaveMesh(ProceduralMeshes.Pillar(15, 2.8f, 21f, false, 8), "Tower");
            for (int i = 0; i < 3; i++) SaveMesh(ProceduralMeshes.DeadTree(i + 21, 6f + i * 1.5f), "DeadTree" + i);
            for (int i = 0; i < 4; i++) SaveMesh(ProceduralMeshes.Gravestone(i + 31), "Grave" + i);
            for (int i = 0; i < 2; i++) SaveMesh(ProceduralMeshes.GrassTuft(i + 41), "Grass" + i);

            // castello
            SaveMesh(ProceduralMeshes.GothicWall(51, 28f, 17f, 2.2f, 4.6f, 0f, 4.6f, 7.2f), "CastleFacade");
            SaveMesh(ProceduralMeshes.GothicWall(52, 8f, 13f, 2f, 2.6f, 4.2f, 8.2f, 10.6f), "HallWallWindow");
            SaveMesh(ProceduralMeshes.GothicWall(53, 32f, 17f, 2.2f, 6f, 5f, 10.5f, 15f), "HallBackWall");
            SaveMesh(ProceduralMeshes.ArchFrame(54, 4.6f, 4.6f, 7.2f, 0.7f, 2.8f), "GateArch");
            SaveMesh(ProceduralMeshes.ArchPane(2.6f, 8.2f, 10.6f, 4.2f), "WindowPane");
            SaveMesh(ProceduralMeshes.ArchPane(6f, 10.5f, 15f, 5f), "RosePane");
            SaveMesh(ProceduralMeshes.FlagstoneFloor(55, 10f, 10f, 1.25f), "FloorTile");
            SaveMesh(ProceduralMeshes.Dais(56, 6.5f, 3, 0.3f, 1.1f), "Dais");
            SaveMesh(ProceduralMeshes.Throne(57), "Throne");
            SaveMesh(ProceduralMeshes.WallTorch(), "WallTorch");
            SaveMesh(ProceduralMeshes.Brazier(), "Brazier");
            SaveMesh(ProceduralMeshes.Banner(58, 1.6f, 4.5f), "Banner");
            SaveMesh(ProceduralMeshes.FogSheet(4.8f, 7.2f, 0.35f), "FogSheet");
            SaveMesh(ProceduralMeshes.Ring(), "ShockRing");
            SaveMesh(ProceduralMeshes.Sword(), "Sword");
            SaveMesh(ProceduralMeshes.Greatsword(), "Greatsword");
            SaveMesh(ArrowMesh(), "Arrow");
            foreach (var r in new[] { "CinderOath", "Thornwraith", "Wyrmfang", "Wyrmwing", "RewardChest", "RewardChestLid" }) BuildRelicMesh(r);

            // travi e tetto spezzato
            var beam = new MeshBuilder { uvScale = 0.8f };
            beam.AddPrism(new Vector3(-16, 0, 0), new Vector3(16, 0, 0), 0.32f, 0.3f, 4, true, true, 3, 0.1f);
            SaveMesh(beam.Build("Beam"), "Beam");
            var roof = new MeshBuilder { uvScale = 0.4f };
            ProceduralMeshes.Box(roof, Vector3.zero, new Vector3(32f, 0.4f, 4f), 61, 0.05f);
            SaveMesh(roof.Build("RoofPanel"), "RoofPanel");
            var ashMound = ProceduralMeshes.Rock(71, 1.1f, 0.22f);
            SaveMesh(ashMound, "AshMound");
        }

        // ================================================================== audio
        const string SoundPack = "Assets/Free Sounds Pack/";
        const string SlashClip = "Assets/a-simple-sword-slash-geookaks.wav";
        const string BossMusic = "Assets/bossfight.mp3";
        static Sfx.Entry[] _sfxEntries;

        static AudioClip Snd(string path, bool stream = false)
        {
            if (!path.StartsWith("Assets/")) path = SoundPack + path;
            if (AssetImporter.GetAtPath(path) is AudioImporter ai)
            {
                var st = ai.defaultSampleSettings;
                st.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                st.compressionFormat = stream ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                st.quality = 0.7f;
                ai.defaultSampleSettings = st;
                ai.forceToMono = !stream;
                ai.loadInBackground = stream;
                ai.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning("[DF] Missing sound: " + path);
            return clip;
        }

        static Sfx.Layer L(string file, float offset = 0f, float vol = 1f, float pitch = 1f) => new Sfx.Layer { clip = Snd(file), offset = offset, volume = vol, pitch = pitch };

        static Sfx.Entry E(SfxId id, Vector2 pitch, float vol, float spatial, params Sfx.Layer[] layers) => new Sfx.Entry { id = id, layers = layers, pitchRange = pitch, volume = vol, spatial = spatial };

        static void ConfigureAudio()
        {
            // sound analysis (onset/peak) → offsets skip leading silence so impacts land on the frame
            _sfxEntries = new[]
            {
                E(SfxId.Swing,      new Vector2(0.9f, 1.12f), 0.85f, 0.8f, L(SlashClip)),
                E(SfxId.HitFlesh,   new Vector2(0.88f, 1.12f), 1f, 0.85f, L("Stab 4-1.wav", 0.105f, 0.9f), L("Hit Generic 5-1.wav", 0f, 0.7f, 0.85f)),
                E(SfxId.HitArmor,   new Vector2(0.9f, 1.1f), 0.8f, 0.85f, L("Shield Metal Impact 1-3.wav", 0.005f, 1f, 0.9f)),
                E(SfxId.Block,      new Vector2(0.92f, 1.08f), 0.9f, 0.85f, L("Shield Metal Impact 1-3.wav", 0.005f), L("Hit Generic 2-1.wav", 0f, 0.4f, 0.8f)),
                E(SfxId.Parry,      new Vector2(0.97f, 1.06f), 1f, 0.7f, L("Blacksmithing 1-1.wav", 0.005f, 1f, 1.1f), L("Shield Metal Impact 1-3.wav", 0.005f, 0.7f, 1.25f)),
                E(SfxId.GuardBreak, new Vector2(0.8f, 0.9f), 1f, 0.85f, L("Shield Metal Impact 1-3.wav", 0.005f, 1f, 0.7f), L("Punch 2-1.wav", 0.008f, 0.9f)),
                E(SfxId.Roll,       new Vector2(0.85f, 1.05f), 0.7f, 0.8f, L("Whoosh 1-1.wav", 0.05f, 0.7f, 0.7f), L("Bag Handle 1-5.wav", 0.14f, 0.8f)),
                E(SfxId.Hurt,       new Vector2(0.9f, 1.1f), 0.9f, 0.85f, L("Punch 2-1.wav", 0.008f), L("Hit Generic 2-1.wav", 0f, 0.8f, 0.85f)),
                E(SfxId.Death,      new Vector2(0.85f, 0.95f), 0.9f, 0.85f, L("Hit Generic 2-1.wav", 0f, 0.9f, 0.6f), L("Rock Impact 37.wav", 0f, 0.5f, 0.6f)),
                E(SfxId.EnemyDeath, new Vector2(0.95f, 1.05f), 0.9f, 0.9f, L("Zombie 1 - Short 1-01.wav", 0.015f, 1f, 0.8f)),
                E(SfxId.EnemyGrunt, new Vector2(0.9f, 1.15f), 0.6f, 0.9f, L("Zombie 1 - Short 1-01.wav", 0.015f, 0.8f, 1.05f)),
                E(SfxId.Roar,       new Vector2(0.95f, 1.03f), 1.2f, 0.6f, L("Creature 1-21.wav", 0.12f, 1f, 0.62f)),
                E(SfxId.Slam,       new Vector2(0.9f, 1.05f), 1.1f, 0.7f, L("Rock Impact 11.wav", 0f, 1f, 0.8f), L("Explosion Medium 2-1.wav", 0.04f, 0.5f, 0.7f)),
                E(SfxId.Quake,      new Vector2(0.9f, 1.0f), 1.2f, 0.5f, L("Rock Large Debris 1-3.wav", 0.24f, 1f, 0.8f), L("Explosion Large 1-1.wav", 0.035f, 0.55f, 0.6f)),
                E(SfxId.FogPass,    new Vector2(0.95f, 1.05f), 0.8f, 0f, L("Magical Interface 8-1.wav", 0.09f, 0.8f, 0.6f)),
                E(SfxId.Drink,      new Vector2(0.95f, 1.05f), 0.8f, 0.6f, L("Magical Interface 5-1.wav", 0.12f, 0.9f, 0.85f)),
                E(SfxId.Victory,    new Vector2(1f, 1f), 0.9f, 0f, L("Magical Interface 8-1.wav", 0.09f, 1f, 0.75f), L("Special Collectible 26-1.wav", 0f, 0.4f, 0.7f)),
                E(SfxId.Respawn,    new Vector2(0.95f, 1.05f), 0.7f, 0.8f, L("Draw Weapon Metal 1-1.wav", 0.1f)),
                E(SfxId.UiClick,    new Vector2(0.95f, 1.05f), 0.5f, 0f, L("Interface 3-3.wav")),
                E(SfxId.UiConfirm,  new Vector2(1f, 1f), 0.6f, 0f, L("Interface 1-1.wav", 0.004f)),
                E(SfxId.FireWhoosh, new Vector2(0.85f, 1.05f), 1f, 0.7f, L("Fire Whoosh 2-15.wav", 0f, 1f, 0.9f)),
                E(SfxId.FireBurst,  new Vector2(0.85f, 0.95f), 1.2f, 0.5f, L("Explosion Large 1-1.wav", 0.035f, 0.9f, 0.8f), L("Fire Whoosh 2-15.wav", 0f, 0.8f, 0.7f)),
                E(SfxId.Eruption,   new Vector2(0.9f, 1.1f), 1f, 0.75f, L("Explosion Medium 2-1.wav", 0.04f, 0.8f, 0.85f), L("Fire Whoosh 2-15.wav", 0f, 0.9f, 1.1f)),
                E(SfxId.Warning,    new Vector2(0.95f, 1.05f), 0.55f, 0.7f, L("Magical Interface 8-1.wav", 0.09f, 0.6f, 0.45f)),
                E(SfxId.Leap,       new Vector2(0.9f, 1.0f), 1f, 0.7f, L("Whoosh 4-1.wav", 0f, 1f, 0.6f), L("Bag Handle 1-5.wav", 0.14f, 0.6f, 0.6f)),
                // bow: tension creak on the draw, dry knock + short whoosh on the release, "thunk" in walls
                E(SfxId.BowDraw,    new Vector2(0.95f, 1.1f), 0.45f, 0.8f, L("Wood and Door Creak 01.wav", 0.06f, 0.7f, 1.8f)),
                E(SfxId.BowRelease, new Vector2(0.92f, 1.08f), 0.9f, 0.75f, L("Wood 14-8.wav", 0.01f, 1f, 1.5f), L("Whoosh 1-1.wav", 0.05f, 0.7f, 1.6f)),
                E(SfxId.ArrowImpact, new Vector2(0.9f, 1.15f), 0.8f, 0.9f, L("Wood Chop 1-4.wav", 0f, 1f, 1.3f), L("Shield Wood Impact 2-1.wav", 0.02f, 0.35f, 1.4f)),
                // reward chest: heavy lid creak, then the relics' chime
                E(SfxId.ChestOpen,  new Vector2(0.95f, 1.02f), 1f, 0.7f, L("Door Open 4-1.wav", 0f, 0.9f, 0.75f), L("Wood and Door Creak 01.wav", 0.06f, 0.55f, 0.85f)),
                E(SfxId.RelicReveal, new Vector2(0.98f, 1.02f), 0.9f, 0.4f, L("Special Collectible 9-1.wav", 0f, 0.9f, 0.9f), L("Magical Interface 8-1.wav", 0.09f, 0.5f, 0.7f)),
            };
            _sfxEntries = _sfxEntries.Concat(CastleSfx()).Concat(MirrorSfx()).ToArray();
            Snd(BossMusic, true);
            Snd("Fire Burning Loop 2.wav", true);
        }

        static void AddFireLoop(Transform parent, float volume, float maxDistance)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPack + "Fire Burning Loop 2.wav");
            if (clip == null) return;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.playOnAwake = true;
            src.volume = volume; src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1.5f; src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
        }

        // ================================================================== animation analysis
        const string ClipAttack1 = "GS_Attack1";   // rising diagonal, low-right → high-left
        const string ClipAttack2 = "GS_Attack2";   // horizontal sweep, left → right
        const string ClipHeavy = "GS_Heavy";       // overhead cleave (raise = charge hold, full-body slam)
        const string ClipSpin = "GS_Spin";         // full clockwise turn, blade extended (loop)

        static AnimationAnalysis.GripInfo _grip;
        static Vector3 _gripPos;

        /// <summary>Generic rig, loops for the cyclic clips, no materials from the FBX.</summary>
        static void ConfigureKnightImport()
        {
            var mi = AssetImporter.GetAtPath(KnightFbx) as ModelImporter;
            if (mi == null) { Debug.LogError("[DF] Missing " + KnightFbx + " (run Tools/gs_anim.py in Blender)"); return; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                bool loop = c.name.Contains("GS_Idle") || c.name.Contains("GS_Walk") || c.name.Contains("GS_Run") || c.name.Contains("GS_Spin") || c.name.Contains("GS_Block");
                c.loopTime = loop;
                c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }
        static CharacterAnimator.SwingWindow[] _swingWindows;
        static readonly Dictionary<string, AnimationAnalysis.SwingInfo> _swing = new Dictionary<string, AnimationAnalysis.SwingInfo>();
        static float _spinLength = 0.67f;

        static void AnalyzeAnimations()
        {
            AnimationAnalysis.ReadSocket(out _gripPos, out _grip.bladeLocal, out _grip.edgeLocal);
            var tmp = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightFbx));
            float h = tmp.GetComponentInChildren<SkinnedMeshRenderer>().bounds.size.y;
            Object.DestroyImmediate(tmp);
            _swing.Clear();
            foreach (var c in new[] { ClipAttack1, ClipAttack2, ClipHeavy, ClipSpin })
                _swing[c] = AnimationAnalysis.AnalyzeSwing(c, _grip.bladeLocal, h * 0.8f);
            var spin = AnimationAnalysis.FindClip(ClipSpin);
            _spinLength = spin ? spin.length : 0.67f;

            var a1 = _swing[ClipAttack1]; var a2 = _swing[ClipAttack2];
            _swingWindows = new[]
            {
                new CharacterAnimator.SwingWindow { state = CharacterAnimator.Attack1, start = a1.swingStart, peak = a1.swingPeak, end = a1.swingEnd },
                new CharacterAnimator.SwingWindow { state = CharacterAnimator.Attack2, start = a2.swingStart, peak = a2.swingPeak, end = a2.swingEnd },
                new CharacterAnimator.SwingWindow { state = CharacterAnimator.Heavy, start = _swing[ClipHeavy].swingStart, peak = _swing[ClipHeavy].swingPeak, end = _swing[ClipHeavy].swingEnd },
                // the spin is one continuous slash: whole loop active, the "whoosh" peaks once per turn
                new CharacterAnimator.SwingWindow { state = CharacterAnimator.Spin, start = 0.02f, peak = _swing[ClipSpin].swingPeak, end = _spinLength - 0.02f, loopLength = _spinLength },
            };
            Debug.Log($"[DF] Socket grip={_gripPos:F3} blade={_grip.bladeLocal:F2} edge={_grip.edgeLocal:F2} | {a1} | {a2} | {_swing[ClipHeavy]} | spin len {_spinLength:F2}");
        }

        /// <summary>
        /// Fills an attack's timings from the analysed swing of its clip (clip seconds).
        /// windupReal = real seconds between the attack start and the blade starting to swing.
        /// </summary>
        static void ApplySwing(AttackData a, float windupReal, bool fullWindup = false)
        {
            string clip = a.state == CharacterAnimator.Attack2 ? ClipAttack2 : a.state == CharacterAnimator.Spin ? ClipSpin
                        : a.state == CharacterAnimator.Heavy ? ClipHeavy : ClipAttack1;
            var s = _swing[clip];
            if (a.state == CharacterAnimator.Spin)
            {
                a.startTime = 0f;
                a.hitStart = 0.04f; a.hitEnd = _spinLength - 0.04f;
                a.swingPeak = s.swingPeak;
                a.comboWindowStart = _spinLength * 0.9f;
                a.cancelTime = _spinLength;
                a.endTime = _spinLength + 0.05f;
                a.lungeStart = 0f; a.lungeEnd = _spinLength * 0.7f;
                return;
            }
            a.startTime = fullWindup ? s.windupStart : Mathf.Max(0f, s.swingStart - windupReal * a.animSpeed);
            a.hitStart = Mathf.Max(a.startTime, s.swingStart - 0.03f);
            a.hitEnd = s.swingEnd + 0.04f;
            a.swingPeak = s.swingPeak;
            a.comboWindowStart = s.swingEnd + 0.03f;
            a.cancelTime = s.swingEnd + 0.22f;
            a.endTime = Mathf.Max(a.cancelTime + 0.05f, Mathf.Min(s.settle + 0.1f, s.length));
            a.lungeStart = Mathf.Max(a.startTime, s.swingStart - 0.18f);
            a.lungeEnd = s.swingEnd;
        }

        // ================================================================== animator
        static AnimationClip Clip(string name)
        {
            var c = AnimationAnalysis.FindClip(name);
            if (c == null) Debug.LogWarning("[DF] Clip not found: " + name);
            return c;
        }

        static AnimatorController BuildAnimator()
        {
            string path = $"{Gen}/Animation/Knight.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("LocoMul", AnimatorControllerParameterType.Float);
            var ps = ctrl.parameters;
            ps[1].defaultFloat = 1f;
            ctrl.parameters = ps;

            var idle = Clip("GS_Idle");
            var walk = Clip("GS_Walk");
            var run = Clip("GS_Run");

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController(CharacterAnimator.Locomotion, out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.9f);
            tree.AddChild(run, 4.1f);
            tree.AddChild(run, 6.5f);
            var ch = tree.children;
            ch[1].timeScale = 1.1f;
            ch[3].timeScale = 1.45f;
            tree.children = ch;
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoMul";
            sm.defaultState = loco;

            void OneShot(string state, string clip, bool returnToLoco = true, float exitTime = 0.92f)
            {
                var s = sm.AddState(state);
                s.motion = Clip(clip);
                s.writeDefaultValues = true;
                if (returnToLoco)
                {
                    var t = s.AddTransition(loco);
                    t.hasExitTime = true;
                    t.exitTime = exitTime;
                    t.duration = 0.2f;
                }
            }
            // attacks: the code ends them at their analysed "endTime"; exit times are only a safety net
            OneShot(CharacterAnimator.Attack1, ClipAttack1, true, 0.99f);
            OneShot(CharacterAnimator.Attack2, ClipAttack2, true, 0.99f);
            OneShot(CharacterAnimator.Heavy, ClipHeavy, true, 0.99f);
            OneShot(CharacterAnimator.Spin, ClipSpin, false);
            OneShot(CharacterAnimator.Parry, "GS_Parry", true, 0.9f);
            OneShot(CharacterAnimator.Hurt, "knight_hurt_left");
            OneShot(CharacterAnimator.Knockdown, "knight_front_attacked_fall", true, 0.98f);
            OneShot(CharacterAnimator.Death, "knight_front_attacked_fall", false);
            OneShot(CharacterAnimator.Taunt, "GS_Roar", true, 0.95f);

            // livello parte superiore: parata con lo scudo mentre le gambe camminano
            var mask = BuildUpperBodyMask();
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var upper = ctrl.layers[1].stateMachine;
            var empty = upper.AddState(CharacterAnimator.EmptyUpper);
            upper.defaultState = empty;
            var block = upper.AddState(CharacterAnimator.BlockUpper);
            block.motion = Clip("GS_Block");   // greatsword held across, edge to the enemy
            var react = upper.AddState(CharacterAnimator.HitReactUpper);
            react.motion = Clip("knight_hurt_left");
            react.speed = 1.4f;

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static AvatarMask BuildUpperBodyMask(string fbx = KnightFbx, string name = "UpperBody")
        {
            string path = $"{Gen}/Animation/{name}.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(path) != null) AssetDatabase.DeleteAsset(path);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var mask = new AvatarMask();
            var all = model.GetComponentsInChildren<Transform>(true);
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(all[i], model.transform);
                mask.SetTransformPath(i, p);
                mask.SetTransformActive(i, p.Contains("Spine"));
            }
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        // ================================================================== relics (meshes modelled in Blender)
        const string RelicDir = Root + "/Art/Relics";

        [System.Serializable] class RelicPart { public string mat; public float[] v; public int[] t; }
        [System.Serializable] class RelicFile { public string name; public RelicPart[] parts; public float tipFwd, tipUp; public float[] hinge; }

        static readonly Dictionary<string, string> RelicMats = new Dictionary<string, string>
        {
            { "dark", "RelicDark" }, { "edge", "RelicEdge" }, { "gold", "RelicGold" }, { "ember", "RelicEmber" },
            { "leather", "RelicLeather" }, { "darkwood", "RelicDarkwood" }, { "bone", "RelicBone" }, { "rune", "RelicRune" },
            { "iron", "RustMetal" }, { "wood", "RottenWood" }, { "glow", "RelicGlow" }, { "crimson", "RelicCrimson" },
        };
        static readonly Dictionary<string, Material[]> RelicMaterials = new Dictionary<string, Material[]>();
        static readonly Dictionary<string, RelicFile> RelicData = new Dictionary<string, RelicFile>();

        /// <summary>
        /// Reads a mesh exported by Tools/relic_models.py (Unity coordinates, flat triangles), one
        /// submesh per material, box-projected UVs. Saved as Generated/Meshes/&lt;name&gt;.asset.
        /// </summary>
        static void BuildRelicMesh(string name)
        {
            string path = $"{RelicDir}/{name}.json";
            if (!File.Exists(path)) { Debug.LogError("[DF] Missing " + path + " (run Tools/relic_models.py → export_all)"); return; }
            var f = JsonUtility.FromJson<RelicFile>(File.ReadAllText(path));
            var order = new List<string>();
            var verts = new List<Vector3>();
            var subs = new Dictionary<string, List<int>>();
            foreach (var part in f.parts)
            {
                if (!subs.ContainsKey(part.mat)) { subs[part.mat] = new List<int>(); order.Add(part.mat); }
                int b = verts.Count;
                for (int i = 0; i < part.v.Length; i += 3) verts.Add(new Vector3(part.v[i], part.v[i + 1], part.v[i + 2]));
                foreach (int t in part.t) subs[part.mat].Add(b + t);
            }
            var uv = new Vector2[verts.Count];
            foreach (var m in order)
            {
                var tl = subs[m];
                for (int i = 0; i < tl.Count; i += 3)
                {
                    Vector3 a = verts[tl[i]], bb = verts[tl[i + 1]], c = verts[tl[i + 2]];
                    Vector3 n = Vector3.Cross(bb - a, c - a);
                    Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    foreach (int k in new[] { tl[i], tl[i + 1], tl[i + 2] })
                    {
                        Vector3 p = verts[k] * 3f;
                        uv[k] = an.y >= an.x && an.y >= an.z ? new Vector2(p.x, p.z) : an.x >= an.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                    }
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.uv = uv;
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(subs[order[i]], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);
            RelicMaterials[name] = order.Select(m => M[RelicMats.TryGetValue(m, out var mn) ? mn : "Steel"]).ToArray();
            RelicData[name] = f;
        }

        static GameObject RelicObject(string mesh, string goName, Transform parent)
        {
            var go = new GameObject(goName);
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Meshes[mesh];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = RelicMaterials[mesh];
            return go;
        }

        /// <summary>Relic shown rising out of the opened chest (visual only, with a light halo).</summary>
        static GameObject BuildRelicDisplay(string mesh, string prefabName, Color halo)
        {
            var root = new GameObject(prefabName);
            var vis = RelicObject(mesh, "Mesh", root.transform);
            vis.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var b = Meshes[mesh].bounds;
            vis.transform.localPosition = -new Vector3(b.center.x, b.center.y, b.center.z);   // turns around its middle
            var lg = new GameObject("Halo"); lg.transform.SetParent(root.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = halo; l.range = 4f; l.intensity = 0f; l.shadows = LightShadows.None;
            var d = root.AddComponent<RelicDisplay>(); d.halo = l;
            return SavePrefab(root, prefabName);
        }

        // ================================================================== archer (import, animator, arrow)
        // Blender timeline (Tools/archer_anim.py, 30 fps): BW_Shot nocks at frame 8 and lets go at 15,
        // BW_Charged nocks at 11, reaches full draw at 24 (held while charging) and lets go at 26.
        const float ShotNock = 8f / 30f, ShotRelease = 15f / 30f;
        const float PowerNock = 11f / 30f, PowerHold = 24f / 30f, PowerRelease = 26f / 30f;

        static AnimationClip ArcherClip(string name)
        {
            var c = AnimationAnalysis.FindClipIn(ArcherFbx, name);
            if (c == null) Debug.LogWarning("[DF] Archer clip not found: " + name);
            return c;
        }

        /// <summary>Generic rig like the knight; adds "BW_Ready" (bow up, arrow nocked) cut from BW_Shot.</summary>
        static void ConfigureArcherImport()
        {
            var mi = AssetImporter.GetAtPath(ArcherFbx) as ModelImporter;
            if (mi == null) { Debug.LogError("[DF] Missing " + ArcherFbx + " (run Tools/archer_anim.py in Blender)"); return; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations.ToList();
            ModelImporterClipAnimation shot = null;
            foreach (var c in clips)
            {
                c.loopTime = c.name.Contains("BW_Idle") || c.name.Contains("BW_Walk") || c.name.Contains("BW_Run");
                c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
                if (c.name.EndsWith("BW_Shot")) shot = c;
            }
            if (shot != null)
                clips.Add(new ModelImporterClipAnimation
                {
                    name = "BW_Ready", takeName = shot.takeName, firstFrame = 9f, lastFrame = 10f, loopTime = true,
                    lockRootRotation = false, lockRootHeightY = false, lockRootPositionXZ = false
                });
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
        }

        static AnimatorController BuildArcherAnimator()
        {
            string path = $"{Gen}/Animation/Archer.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("LocoMul", AnimatorControllerParameterType.Float);
            var ps = ctrl.parameters;
            ps[1].defaultFloat = 1f;
            ctrl.parameters = ps;

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController(CharacterAnimator.Locomotion, out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.AddChild(ArcherClip("BW_Idle"), 0f);
            tree.AddChild(ArcherClip("BW_Walk"), 1.9f);
            tree.AddChild(ArcherClip("BW_Run"), 4.1f);
            tree.AddChild(ArcherClip("BW_Run"), 6.5f);
            var ch = tree.children;
            ch[1].timeScale = 1.1f;
            ch[3].timeScale = 1.45f;
            tree.children = ch;
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoMul";
            sm.defaultState = loco;

            void OneShot(string state, string clip, bool returnToLoco = true, float exitTime = 0.92f)
            {
                var st = sm.AddState(state);
                st.motion = ArcherClip(clip);
                st.writeDefaultValues = true;
                if (!returnToLoco) return;
                var t = st.AddTransition(loco);
                t.hasExitTime = true; t.exitTime = exitTime; t.duration = 0.2f;
            }
            // same state names as the knight: PlayerController / CharacterAnimator are shared
            OneShot(CharacterAnimator.Attack1, "BW_Shot", true, 0.99f);
            OneShot(CharacterAnimator.Attack2, "BW_Shot", true, 0.99f);
            OneShot(CharacterAnimator.Heavy, "BW_Charged", true, 0.99f);
            OneShot(CharacterAnimator.Spin, "BW_Shot", true, 0.99f);
            OneShot(CharacterAnimator.Parry, "BW_Idle", true, 0.5f);
            OneShot(CharacterAnimator.Hurt, "knight_hurt_left");
            OneShot(CharacterAnimator.Knockdown, "knight_front_attacked_fall", true, 0.98f);
            OneShot(CharacterAnimator.Death, "knight_front_attacked_fall", false);
            OneShot(CharacterAnimator.Taunt, "BW_Idle", true, 0.5f);

            // upper body: bow raised with an arrow on the string while aiming on the move
            var mask = BuildUpperBodyMask(ArcherFbx, "ArcherUpperBody");
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var upper = ctrl.layers[1].stateMachine;
            upper.defaultState = upper.AddState(CharacterAnimator.EmptyUpper);
            upper.AddState(CharacterAnimator.BlockUpper).motion = ArcherClip("BW_Ready");
            var react = upper.AddState(CharacterAnimator.HitReactUpper);
            react.motion = ArcherClip("knight_hurt_left");
            react.speed = 1.4f;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static Vector2 SwatchUV(float px, float py) => new Vector2(px / 2048f, 1f - py / 2048f);

        /// <summary>
        /// Arrow along +Z, origin at the nock (tail), tip at Arrow.Length. UVs sample the wood /
        /// feather / iron swatches of the archer texture, so it shares the archer's material.
        /// </summary>
        static Mesh ArrowMesh()
        {
            var mb = new MeshBuilder();
            Vector2 wood = SwatchUV(1824, 880), feather = SwatchUV(1952, 845), iron = SwatchUV(1952, 915);
            Vector2 d = new Vector2(0.012f, 0.012f);
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, Vector2 uv, bool twoSided = false)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                mb.AddQuad(a, b, c, e, n, uv - d, uv + new Vector2(d.x, -d.y), uv + d, uv + new Vector2(-d.x, d.y));
                if (twoSided) mb.AddQuad(a, b, c, e, -n, uv - d, uv + new Vector2(d.x, -d.y), uv + d, uv + new Vector2(-d.x, d.y));
            }
            float L = Arrow.Length, r = 0.0085f, headStart = L - 0.085f;
            // shaft: square prism
            Vector3[] sq = { new Vector3(r, 0, 0), new Vector3(0, r, 0), new Vector3(-r, 0, 0), new Vector3(0, -r, 0) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = sq[i], b = sq[(i + 1) % 4];
                Quad(a + Vector3.forward * 0.005f, b + Vector3.forward * 0.005f, b + Vector3.forward * headStart, a + Vector3.forward * headStart, wood);
            }
            // broad iron head: flat diamond, 2 faces each side
            float hw = 0.024f, ht = 0.007f;
            Vector3 hb = Vector3.forward * headStart, tip = Vector3.forward * L, barb = Vector3.forward * (headStart + 0.03f);
            Vector3[] rim = { barb + Vector3.right * hw, hb + Vector3.up * ht, barb - Vector3.right * hw, hb - Vector3.up * ht };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = rim[i], b = rim[(i + 1) % 4];
                Vector3 n = Vector3.Cross(b - a, tip - a);
                mb.AddTri(a, b, tip, n, iron, iron + new Vector2(d.x, 0), iron + new Vector2(0, d.y));
                mb.AddTri(a, b, hb - Vector3.forward * 0.012f, -n, iron, iron + new Vector2(d.x, 0), iron + new Vector2(0, d.y));
            }
            // fletching: three swept vanes
            for (int k = 0; k < 3; k++)
            {
                Vector3 dir = Quaternion.AngleAxis(k * 120f + 90f, Vector3.forward) * Vector3.right;
                Vector3 a = Vector3.forward * 0.03f + dir * r, b = Vector3.forward * 0.17f + dir * r;
                Vector3 c = Vector3.forward * 0.13f + dir * 0.032f, e = Vector3.forward * 0.02f + dir * 0.03f;
                Quad(a, b, c, e, feather, true);
            }
            return mb.Build("Arrow");
        }

        // ================================================================== prefab
        public class Prefabs { public GameObject player, archer, bard, arrow, guardian, boss, arena, shockwave, lobby, chest, dragonChest, progress, gaoler, bowman, warden, dragon, dragonArena,
            radiantChest, wraith, revenant, mirror, mirrorArena; }

        /// <summary>
        /// Embers and heat that ride a dragon weapon while it is equipped: world-space particles so a
        /// swing leaves a wake of sparks, plus a flickering ember light.
        /// </summary>
        static GameObject DragonEmbers(Transform parent, Vector3 localPos, Vector3 box, float rate, float lightRange)
        {
            var go = new GameObject("DragonFx");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 200;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 1f), new Color(1f, 0.28f, 0.06f, 1f));
            main.gravityModifier = -0.12f;
            var em = ps.emission; em.rateOverTime = rate; em.rateOverDistance = rate * 0.6f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var no = ps.noise; no.enabled = true; no.strength = 0.35f; no.frequency = 1.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1) },
                      new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
            var lg = new GameObject("Heat"); lg.transform.SetParent(go.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.38f, 0.12f); l.range = lightRange; l.intensity = 1.8f; l.shadows = LightShadows.None;
            lg.AddComponent<FlickerLight>();
            go.SetActive(false);
            return go;
        }

        static Prefabs BuildPrefabs(AnimatorController ctrl, AnimatorController archerCtrl)
        {
            var p = new Prefabs();

            // ---- onda d'urto (solo visuale, non di rete)
            {
                var go = new GameObject("Shockwave");
                var vis = new GameObject("Ring");
                vis.transform.SetParent(go.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = Meshes["ShockRing"];
                var mr = vis.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M["ShockRing"];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.AddComponent<ShockwaveVisual>();
                p.shockwave = SavePrefab(go, "Shockwave");
            }

            // ---- giocatore
            {
                var root = BuildCharacter("Player", M["KnightPlayer"], 1.85f, ctrl, out var ca, out var hitbox, out var lockPoint);
                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<NetworkTransform>();
                ConfigureNT(nt, true);
                var vit = root.AddComponent<PlayerVitals>();
                var pc = root.AddComponent<PlayerController>();
                pc.buffs = root.AddComponent<PlayerBuffs>();
                pc.anim = ca; pc.weapon = hitbox; pc.vitals = vit; pc.lockPoint = lockPoint;
                ApplySwing(pc.lightCombo[0], 0.22f);
                ApplySwing(pc.lightCombo[1], 0.12f);
                ApplySwing(pc.lightCombo[2], 0f);
                ApplySwing(pc.heavyAttack, 0f, true);
                pc.heavyHoldTime = Mathf.Max(pc.heavyAttack.startTime + 0.1f, _swing[ClipHeavy].swingStart - 0.1f);
                ca.swingPitch = 1.05f;
                // relic greatsword: same grip frame as the starting blade (blade +Y, edge ±Z, origin = grip)
                var sw = FindDeep(root.transform, "Greatsword");
                if (sw && Meshes.ContainsKey("CinderOath"))
                {
                    var relic = RelicObject("CinderOath", "CinderOath", sw);
                    relic.SetActive(false);
                    pc.baseWeaponRenderer = sw.GetComponent<MeshRenderer>();
                    pc.relicWeapon = relic;
                    if (Meshes.ContainsKey("Wyrmfang"))
                    {
                        var drg = RelicObject("Wyrmfang", "Wyrmfang", sw);
                        drg.SetActive(false);
                        pc.dragonWeapon = drg;
                        pc.dragonFx = DragonEmbers(sw, new Vector3(0f, 0.95f, 0f), new Vector3(0.03f, 1.4f, 0.16f), 28f, 3.5f);
                    }
                }
                p.player = SavePrefab(root, "Player");
            }

            // ---- arrow (local projectile, not a network object: every client flies its own copy)
            {
                var go = new GameObject("ArrowProjectile");
                go.AddComponent<MeshFilter>().sharedMesh = Meshes["Arrow"];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M["ArcherPlayer"];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var arrow = go.AddComponent<Arrow>();
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.12f;
                tr.minVertexDistance = 0.1f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0, 0.035f), new Keyframe(1, 0f));
                tr.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(0.95f, 0.9f, 0.8f), 0), new GradientColorKey(new Color(0.8f, 0.75f, 0.65f), 1) },
                    alphaKeys = new[] { new GradientAlphaKey(0.45f, 0), new GradientAlphaKey(0f, 1) }
                };
                tr.sharedMaterial = M["SlashTrail"];
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.emitting = false;
                arrow.trail = tr;
                p.arrow = SavePrefab(go, "ArrowProjectile");
            }

            // ---- archer (playable class): hooded Solus with the war-bow, same controller as the knight
            {
                var root = BuildCharacter("PlayerArcher", M["ArcherPlayer"], 1.85f, archerCtrl, out var ca, out _, out var lockPoint, ArcherFbx, "BW_Idle", false);
                root.AddComponent<NetworkObject>();
                ConfigureNT(root.AddComponent<NetworkTransform>(), true);
                var vit = root.AddComponent<PlayerVitals>();
                vit.maxHealth = 110f;                 // lighter armour than the knight
                var pc = root.AddComponent<PlayerController>();
                pc.buffs = root.AddComponent<PlayerBuffs>();
                pc.playerClass = PlayerClass.Archer;
                pc.anim = ca; pc.weapon = null; pc.vitals = vit; pc.lockPoint = lockPoint;
                pc.armorWeight = 1.1f;
                pc.runSpeed = 4.3f;
                pc.sprintSpeed = 6.5f;
                pc.rollDistance = 4.8f;
                pc.rollStamina = 15f;
                // times are clip seconds from the Blender timeline (see ShotNock/ShotRelease…)
                pc.lightCombo = new[]
                {
                    new AttackData
                    {
                        name = "Quick Shot", state = CharacterAnimator.Attack1, animSpeed = 1.0f,
                        startTime = 0f, chainStartTime = 0.15f,
                        hitStart = ShotRelease, hitEnd = ShotRelease, swingPeak = ShotRelease,
                        comboWindowStart = 0.57f, cancelTime = 0.6f, endTime = 0.86f,
                        damage = 13, poiseDamage = 8, staminaCost = 10, knockback = 1.2f, lunge = 0f
                    }
                };
                pc.heavyAttack = new AttackData
                {
                    name = "Power Shot", state = CharacterAnimator.Heavy, animSpeed = 1.1f, startTime = 0f,
                    hitStart = PowerRelease, hitEnd = PowerRelease, swingPeak = PowerRelease,
                    comboWindowStart = 0.95f, cancelTime = 0.95f, endTime = 1.35f,
                    damage = 32, poiseDamage = 34, staminaCost = 24, knockback = 3.4f, lunge = 0f
                };
                pc.heavyHoldTime = PowerHold;
                pc.heavyMaxCharge = 1.0f;
                pc.heavyChargeBonus = 1.1f;
                pc.aimedShotStart = 0.3f;

                // ---- the bow: sockets exported with the animations (Tools/archer_anim.py → make_bow)
                var model = root.GetComponentInChildren<Animator>().transform;
                var bowGo = new GameObject("Bow");
                bowGo.transform.SetParent(root.transform, false);
                var bow = bowGo.AddComponent<ArcherBow>();
                bow.anim = ca;
                bow.grip = FindDeep(model, "BowSocket");
                bow.bowTop = FindDeep(model, "BowTop");
                bow.bowBottom = FindDeep(model, "BowBottom");
                bow.nock = FindDeep(model, "NockSocket");
                if (!bow.grip || !bow.bowTop || !bow.bowBottom || !bow.nock) Debug.LogError("[DF] Archer.fbx: bow socket bones missing (BowSocket/BowTop/BowBottom/NockSocket)");
                var lr = bowGo.AddComponent<LineRenderer>();
                lr.positionCount = 3;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.011f;
                lr.numCapVertices = 0;
                lr.sharedMaterial = M["BowString"];
                lr.shadowCastingMode = ShadowCastingMode.Off;
                bow.stringLine = lr;
                var nocked = new GameObject("NockedArrow");
                nocked.transform.SetParent(root.transform, false);
                nocked.AddComponent<MeshFilter>().sharedMesh = Meshes["Arrow"];
                nocked.AddComponent<MeshRenderer>().sharedMaterial = M["ArcherPlayer"];
                bow.nockedArrow = nocked.transform;
                bow.arrowPrefab = p.arrow;
                bow.draws = new[]
                {
                    new ArcherBow.DrawWindow { state = CharacterAnimator.Attack1, nock = ShotNock, release = ShotRelease },
                    new ArcherBow.DrawWindow { state = CharacterAnimator.Heavy, nock = PowerNock, release = PowerRelease },
                };
                pc.bow = bow;

                // starting bow (its own skinned mesh) ↔ relic Thornwraith, fitted on the string bones
                var baseBow = FindDeep(model, "ArcherBow");
                if (baseBow) pc.baseWeaponRenderer = baseBow.GetComponent<Renderer>();
                var fwdBone = FindDeep(model, "BowFwd");
                if (bow.grip && bow.bowTop && bow.bowBottom && fwdBone && Meshes.ContainsKey("Thornwraith"))
                {
                    var data = RelicData["Thornwraith"];
                    Vector3 top = bow.bowTop.position, bot = bow.bowBottom.position;
                    Vector3 up = (top - bot).normalized;
                    Vector3 fwd = Vector3.ProjectOnPlane(fwdBone.position - bow.grip.position, up).normalized;
                    float scale = (top - bot).magnitude / (2f * data.tipUp);
                    var relic = RelicObject("Thornwraith", "Thornwraith", null);
                    relic.transform.SetPositionAndRotation((top + bot) * 0.5f - fwd * data.tipFwd * scale, Quaternion.LookRotation(fwd, up));
                    relic.transform.localScale = Vector3.one * scale;
                    relic.transform.SetParent(bow.grip, true);
                    relic.SetActive(false);
                    pc.relicWeapon = relic;
                    if (Meshes.ContainsKey("Wyrmwing"))
                    {
                        var drg = RelicObject("Wyrmwing", "Wyrmwing", null);
                        drg.transform.SetPositionAndRotation(relic.transform.position, relic.transform.rotation);
                        drg.transform.localScale = relic.transform.lossyScale;
                        drg.transform.SetParent(bow.grip, true);
                        drg.SetActive(false);
                        pc.dragonWeapon = drg;
                        var fx = DragonEmbers(drg.transform, Vector3.zero, new Vector3(0.04f, 1.4f, 0.25f), 22f, 3f);
                        fx.transform.SetParent(bow.grip, true);
                        pc.dragonFx = fx;
                    }
                }
                else Debug.LogWarning("[DF] Archer relic bow not fitted (bow bones or Thornwraith mesh missing)");
                p.archer = SavePrefab(root, "PlayerArcher");
            }

            // ---- bard (playable class): hooded wanderer with a singing staff (Tools/bard_anim.py)
            BuildBardPrefab(p, BuildBardAnimator());

            // ---- guardiano corrotto
            {
                var root = BuildCharacter("Guardian", M["KnightGuardian"], 1.9f, ctrl, out var ca, out var hitbox, out var lockPoint);
                root.AddComponent<NetworkObject>();
                ConfigureNT(root.AddComponent<NetworkTransform>(), false);
                var ai = root.AddComponent<GuardianAI>();
                ai.anim = ca; ai.weapon = hitbox;
                ai.displayName = "Corrupted Guardian";
                ApplySwing(ai.slash, 0.45f);
                ApplySwing(ai.sweep, 0.35f);
                ApplySwing(ai.overhead, 0f, true);
                ca.swingPitch = 0.9f;
                var lot = root.AddComponent<LockOnTarget>();
                lot.lockPoint = lockPoint;
                p.guardian = SavePrefab(root, "Guardian");
            }

            // ---- boss
            {
                float h = 1.85f * 2.1f;
                var root = BuildCharacter("Boss", M["KnightBoss"], h, ctrl, out var ca, out var hitbox, out var lockPoint);
                root.AddComponent<NetworkObject>();
                ConfigureNT(root.AddComponent<NetworkTransform>(), false);
                var ai = root.AddComponent<BossAI>();
                ai.anim = ca; ai.weapon = hitbox;
                ai.displayName = "The Ashen King";
                ApplySwing(ai.cleave, 0.55f);
                ApplySwing(ai.sweep, 0.6f);
                ApplySwing(ai.overhead, 0f, true);
                ApplySwing(ai.lungeCleave, 0.75f);
                ApplySwing(ai.leapSlam, 0f, true);
                ApplySwing(ai.waveSlash, 0.55f);
                ApplySwing(ai.turnSweep, 0.35f);
                ApplySwing(ai.plunge, 0f, true);
                ApplySwing(ai.whirlwind, 0f);
                ca.swingPitch = 0.62f;
                ca.swingVolume = 1.2f;
                ai.baseHealth = 1150f;
                ai.basePoise = 170f;
                ai.poiseRegenPerSecond = 18f;
                ai.walkSpeed = 1.6f;
                ai.chaseSpeed = 3.6f;
                ai.turnSpeed = 150f;
                ai.staggerDuration = 2.2f;
                ai.parryStaggerDuration = 2.6f;
                ai.shockwavePrefab = p.shockwave;
                var lot = root.AddComponent<LockOnTarget>();
                lot.lockPoint = lockPoint;
                lot.framingSize = 2f;
                lot.enabled = false;
                p.boss = SavePrefab(root, "Boss");
            }

            // ---- reward chests (network) + the weapons that rise out of them: the King's relics, the wyrm's hoard
            if (Meshes.ContainsKey("RewardChest"))
            {
                GameObject Chest(byte tier)
                {
                    bool dragon = tier == Inventory.Dragon;
                    var root = new GameObject(dragon ? "DragonHoard" : "RewardChest");
                    root.AddComponent<NetworkObject>();
                    var rc = root.AddComponent<RewardChest>();
                    rc.tier = tier;
                    var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
                    RelicObject("RewardChest", "Body", visual);
                    var pivot = new GameObject("LidPivot").transform; pivot.SetParent(visual, false);
                    var h = RelicData["RewardChestLid"].hinge;
                    pivot.localPosition = new Vector3(h[0], h[1], h[2]);
                    RelicObject("RewardChestLid", "Lid", pivot);
                    var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0.75f, 0.1f);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = dragon ? 9f : 7f; l.intensity = 0f; l.shadows = LightShadows.None;
                    l.color = dragon ? new Color(1f, 0.38f, 0.14f) : new Color(1f, 0.72f, 0.38f);
                    var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0.3f, 0); box.size = new Vector3(1.12f, 0.62f, 0.72f);
                    rc.lidPivot = pivot; rc.glowLight = l;
                    Color halo = dragon ? new Color(1f, 0.35f, 0.12f) : new Color(1f, 0.55f, 0.25f);
                    if (dragon)
                    {
                        // scorched: crimson where the King's chest has gold, embers drifting out of it
                        foreach (var r in visual.GetComponentsInChildren<MeshRenderer>())
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) if (mats[i] == M["RelicGold"]) mats[i] = M["RelicCrimson"];
                            r.sharedMaterials = mats;
                        }
                        DragonEmbers(root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 0.3f, 0.6f), 30f, 6f).SetActive(true);
                        rc.swordDisplay = BuildRelicDisplay("Wyrmfang", "DragonDisplaySword", halo);
                        rc.bowDisplay = BuildRelicDisplay("Wyrmwing", "DragonDisplayBow", halo);
                        if (Meshes.ContainsKey("StaffDragonDisplay")) rc.staffDisplay = BuildRelicDisplay("StaffDragonDisplay", "DragonDisplayStaff", halo);
                    }
                    else
                    {
                        rc.swordDisplay = BuildRelicDisplay("CinderOath", "RelicDisplaySword", halo);
                        rc.bowDisplay = BuildRelicDisplay("Thornwraith", "RelicDisplayBow", new Color(0.55f, 0.85f, 1f));
                        if (Meshes.ContainsKey("StaffRelicDisplay")) rc.staffDisplay = BuildRelicDisplay("StaffRelicDisplay", "RelicDisplayStaff", new Color(1f, 0.62f, 0.3f));
                    }
                    return SavePrefab(root, dragon ? "DragonHoard" : "RewardChest");
                }
                p.chest = Chest(Inventory.Relic);
                if (Meshes.ContainsKey("Wyrmfang") && Meshes.ContainsKey("Wyrmwing")) p.dragonChest = Chest(Inventory.Dragon);
            }

            // ---- lobby state (names, lobby name, started flag) — spawned by the host when the lobby opens
            {
                var root = new GameObject("LobbyState");
                root.AddComponent<NetworkObject>();
                root.AddComponent<LobbyState>();
                p.lobby = SavePrefab(root, "LobbyState");
            }

            // ---- arena + cancello di nebbia
            {
                var root = new GameObject("BossArena");
                root.AddComponent<NetworkObject>();
                var arena = root.AddComponent<BossArena>();
                var fog = new GameObject("FogGate");
                fog.transform.SetParent(root.transform, false);
                fog.AddComponent<MeshFilter>().sharedMesh = Meshes["FogSheet"];
                var mr = fog.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M["FogGate"];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var scroll = fog.AddComponent<UVScroll>();
                scroll.speed = new Vector2(0.02f, 0.05f);
                var colGo = new GameObject("Barrier");
                colGo.transform.SetParent(root.transform, false);
                var box = colGo.AddComponent<BoxCollider>();
                box.center = new Vector3(0, 3.6f, 0.1f);
                box.size = new Vector3(5f, 7.4f, 0.6f);
                var lightGo = new GameObject("FogLight");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition = new Vector3(0, 3f, -1.2f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point; l.range = 7f; l.intensity = 1.2f; l.color = new Color(0.75f, 0.75f, 0.7f);
                arena.fogRenderer = mr; arena.fogCollider = box; arena.fogLight = l;
                p.arena = SavePrefab(root, "BossArena");
            }
            BuildCastlePrefabs(p, ctrl, archerCtrl);
            BuildMirrorPrefabs(p, ctrl);
            return p;
        }

        static void ConfigureNT(NetworkTransform nt, bool ownerAuthority)
        {
            nt.AuthorityMode = ownerAuthority ? NetworkTransform.AuthorityModes.Owner : NetworkTransform.AuthorityModes.Server;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            string path = $"{Gen}/Prefabs/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        /// <summary>
        /// Personaggio basato sul cavaliere Solus: scala all'altezza voluta, materiale, animator,
        /// spada nella mano destra e scudo sull'avambraccio sinistro (orientati calcolando gli
        /// assi della mano dalle nocche del rig), hitbox a capsula sull'arma, CharacterController.
        /// </summary>
        static GameObject BuildCharacter(string name, Material mat, float height, AnimatorController ctrl, out CharacterAnimator ca, out WeaponHitbox hitbox, out Transform lockPoint,
                                         string fbxPath = KnightFbx, string idleClipName = "GS_Idle", bool withSword = true)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var model = (GameObject)Object.Instantiate(fbx);
            model.name = "Knight";
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // orientation: the character must face +Z. Measured on the idle pose from the feet
            // (heel → toes), which is independent of how the FBX axes were exported.
            var idle0 = AnimationAnalysis.FindClipIn(fbxPath, idleClipName);
            if (idle0) idle0.SampleAnimation(model, 0f);
            var foot0 = FindDeep(model.transform, "Foot_L"); var toes0 = FindDeep(model.transform, "Toes_L");
            if (foot0 && toes0 && (toes0.position - foot0.position).z < 0f) model.transform.rotation = Quaternion.Euler(0, 180, 0);

            // the body is the tallest skinned mesh (the archer's bow is a separate one)
            var smr = model.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.bounds.size.y).FirstOrDefault();
            Bounds b = smr != null ? smr.bounds : new Bounds(Vector3.up, Vector3.one * 2);
            float s = height / Mathf.Max(0.01f, b.size.y);

            var root = new GameObject(name);
            float pivot = height * 0.5f;
            var visual = new GameObject("VisualRoot").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0, pivot, 0);
            var offset = new GameObject("ModelOffset").transform;
            offset.SetParent(visual, false);
            offset.localPosition = new Vector3(-b.center.x * s, -pivot - b.min.y * s, -b.center.z * s);
            offset.localScale = Vector3.one * s;
            model.transform.SetParent(offset, false);

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen = true; sk.quality = SkinQuality.Bone2; }
            }

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            float k = height / 1.85f;

            // idle pose as rest pose (same reference used by the clip analysis / in-place lock)
            var idleClip = AnimationAnalysis.FindClipIn(fbxPath, idleClipName);
            if (idleClip) idleClip.SampleAnimation(model, 0f);

            hitbox = null;
            BladeTrail trail = null;
            if (withSword) BuildSword(model, root, animator, k, out hitbox, out trail);

            // ---- componenti radice
            var cc = root.AddComponent<CharacterController>();
            cc.height = height * 0.95f;
            cc.radius = 0.36f * k;
            cc.center = new Vector3(0, height * 0.5f, 0);
            cc.slopeLimit = 48f;
            cc.stepOffset = 0.35f * Mathf.Min(k, 1.5f);
            cc.skinWidth = 0.04f;

            ca = root.AddComponent<CharacterAnimator>();
            ca.animator = animator;
            ca.visualRoot = visual;
            ca.pivotHeight = pivot;
            ca.locomotionScale = k;
            ca.weaponTrail = trail;
            ca.inPlaceBones = AnimationAnalysis.InPlaceBones(model.transform);
            ca.swings = withSword ? _swingWindows : new CharacterAnimator.SwingWindow[0];

            lockPoint = new GameObject("LockPoint").transform;
            lockPoint.SetParent(root.transform, false);
            lockPoint.localPosition = new Vector3(0, height * 0.72f, 0);
            // wading splashes in the flooded castle corridors
            var steps = root.AddComponent<Footsteps>();
            steps.scale = k;
            steps.footL = FindDeep(model.transform, "Foot_L"); steps.footR = FindDeep(model.transform, "Foot_R");
            return root;
        }

        /// <summary>Greatsword in the right hand, capsule hitbox along the blade and the slash trail.</summary>
        static void BuildSword(GameObject model, GameObject root, Animator animator, float k, out WeaponHitbox hitbox, out BladeTrail trail)
        {
            // ---- greatsword, grip solved from the attack clips (see AnimationAnalysis.SolveGrip)
            var sword = new GameObject("Greatsword");
            sword.AddComponent<MeshFilter>().sharedMesh = Meshes["Greatsword"];
            sword.AddComponent<MeshRenderer>().sharedMaterial = M["Steel"];
            var handR = FindDeep(model.transform, "hand_R");
            if (handR)
            {
                // grip authored with the animations: right hand under the guard, edge along the knuckles
                sword.transform.SetParent(handR, false);
                sword.transform.localRotation = Quaternion.LookRotation(_grip.edgeLocal, _grip.bladeLocal);
                sword.transform.localPosition = _gripPos;
                SetWorldScale(sword.transform, k);
                // the mesh origin sits 3 cm above the right hand's grip point
                sword.transform.position += sword.transform.up * 0.03f * k;
            }
            else
            {
                Debug.LogWarning("[DF] hand_R not found: sword attached to the root");
                sword.transform.SetParent(root.transform, false);
                sword.transform.localPosition = new Vector3(0.4f, 1f, 0.3f) * k;
            }
            var basePt = new GameObject("HitboxBase").transform; basePt.SetParent(sword.transform, false); basePt.localPosition = new Vector3(0, 0.2f, 0);
            var tipPt = new GameObject("HitboxTip").transform; tipPt.SetParent(sword.transform, false); tipPt.localPosition = new Vector3(0, 1.5f, 0);
            hitbox = sword.AddComponent<WeaponHitbox>();
            hitbox.basePoint = basePt; hitbox.tipPoint = tipPt; hitbox.radius = 0.18f * k;
            var relay = animator.gameObject.AddComponent<AnimationEventRelay>();
            relay.hitbox = hitbox;

            // ---- slash arc swept between hilt and tip (shown only during the real swing frames)
            trail = sword.AddComponent<BladeTrail>();
            var trailBase = new GameObject("TrailBase").transform; trailBase.SetParent(sword.transform, false); trailBase.localPosition = new Vector3(0, 0.22f, 0);
            trail.basePoint = trailBase;
            trail.tipPoint = tipPt;
            trail.material = M["SlashTrail"];
            trail.lifetime = 0.22f;
        }

        static void SetWorldScale(Transform t, float worldScale)
        {
            Vector3 ps = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(worldScale / Mathf.Max(1e-5f, Mathf.Abs(ps.x)), worldScale / Mathf.Max(1e-5f, Mathf.Abs(ps.y)), worldScale / Mathf.Max(1e-5f, Mathf.Abs(ps.z)));
        }

        // ================================================================== scena
        static Transform _envRoot;

        static GameObject Place(string name, string mesh, string mat, Vector3 pos, Quaternion rot, Transform parent, bool collider = true, float scale = 1f, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = Meshes[mesh];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = M[mat];
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = Meshes[mesh];
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            return go;
        }

        static float H(float x, float z) => ProceduralMeshes.LevelHeight(x, z);
        static Vector3 G(float x, float z, float yOff = 0f) => new Vector3(x, H(x, z) + yOff, z);
        static Quaternion Yaw(float deg) => Quaternion.Euler(0, deg, 0);

        static void BuildScene(UISkin skin, Prefabs prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rng = new System.Random(1337);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---- atmosfera
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.38f, 0.37f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.4f, 0.42f, 0.38f);
            RenderSettings.fogDensity = 0.034f;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            var moonGo = new GameObject("Moonlight");
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.66f, 0.74f);
            moon.intensity = 1.15f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.85f;
            moonGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = moon;

            // ---- post-processing
            var volGo = new GameObject("PostProcess_Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildVolumeProfile();

            _envRoot = new GameObject("Environment").transform;
            var outdoor = new GameObject("Outdoor_Ruins").transform; outdoor.SetParent(_envRoot);
            var castle = new GameObject("Castle_ThroneRoom").transform; castle.SetParent(_envRoot);

            // ---- terreno
            Place("Terrain", "Terrain", "Ground", Vector3.zero, Quaternion.identity, outdoor, true, 1f, false).GetComponent<MeshRenderer>().receiveShadows = true;

            // ---- falò (spawn)
            Vector3 bonfire = G(0, -62);
            var bf = new GameObject("Bonfire").transform; bf.SetParent(outdoor); bf.position = bonfire;
            Place("Ash", "AshMound", "Ash", bonfire, Quaternion.identity, bf, false, 1f);
            Place("BonfireSword", "Sword", "RustMetal", bonfire + new Vector3(0, 0.95f, 0), Quaternion.Euler(0, 30, 180), bf, false, 1.1f);
            Fx.CreateFlame(bf, new Vector3(0, 0.15f, 0), 1.6f, M["ParticleAdd"]);
            AddFireLoop(bf, 0.55f, 14f);
            AddLight(bf, new Vector3(0, 1.2f, 0), new Color(1f, 0.55f, 0.25f), 3.2f, 11f, true);
            bf.gameObject.AddComponent<CheckpointFire>().index = 0;   // rest here too (heal, flasks, enemies restored)

            // ---- rovine lungo il sentiero
            (float x, float z, float yaw, int w)[] walls =
            {
                (-9, -44, 20, 0), (9, -38, -15, 1), (-13, -26, 75, 2), (12, -20, 95, 3), (-7, -8, 10, 1),
                (15, -4, -30, 0), (-16, 4, 60, 3), (7, 8, 160, 2), (-20, -50, 40, 2), (21, -30, 110, 0), (-24, -12, 85, 1), (24, 10, 70, 3)
            };
            int wi = 0;
            foreach (var w in walls)
                Place("RuinWall_" + wi++, "RuinWall" + w.w, "StoneRuins", G(w.x, w.z, -0.3f), Yaw(w.yaw), outdoor);

            // colonnato in rovina
            for (int i = 0; i < 5; i++)
            {
                float z = -32 + i * 5.5f;
                foreach (float x in new[] { -6.5f, 6.5f })
                {
                    if (rng.NextDouble() < 0.2) continue;
                    string pm = rng.NextDouble() < 0.6 ? (rng.NextDouble() < 0.5 ? "PillarBroken0" : "PillarBroken1") : "PillarRuin";
                    Place("Column", pm, "StoneRuins", G(x + R(-0.3f, 0.3f), z, -0.1f), Yaw(R(0, 360)), outdoor);
                }
            }
            // colonna caduta
            Place("FallenColumn", "PillarRuin", "StoneRuins", G(-3.5f, -22, 0.45f), Quaternion.Euler(0, 35, 86), outdoor);

            // lapidi
            for (int i = 0; i < 14; i++)
            {
                float x = -16 + R(-6, 6), z = -54 + R(-7, 7);
                Place("Grave", "Grave" + (i % 4), "StoneRuins", G(x, z, -0.05f), Quaternion.Euler(R(-12, 12), R(-25, 25), R(-10, 10)), outdoor, true);
            }
            for (int i = 0; i < 8; i++)
            {
                float x = 17 + R(-5, 5), z = -14 + R(-6, 6);
                Place("Grave", "Grave" + (i % 4), "StoneRuins", G(x, z, -0.05f), Quaternion.Euler(R(-12, 12), R(160, 200), R(-10, 10)), outdoor, true);
            }

            // alberi morti
            for (int i = 0; i < 34; i++)
            {
                float a = R(0, Mathf.PI * 2);
                float x = Mathf.Cos(a) * R(14, 40), z = -25 + Mathf.Sin(a) * R(18, 55);
                if (z > 14 && Mathf.Abs(x) < 22) continue;
                if (Mathf.Abs(x - Mathf.Sin(z * 0.06f) * 5f) < 4f) continue; // non sul sentiero
                var tree = Place("DeadTree", "DeadTree" + (i % 3), "Bark", G(x, z, -0.2f), Yaw(R(0, 360)), outdoor, false, R(0.8f, 1.3f));
                var cap = tree.AddComponent<CapsuleCollider>(); cap.radius = 0.3f; cap.height = 4f; cap.center = Vector3.up * 2f;
            }

            // rocce
            for (int i = 0; i < 40; i++)
            {
                float x = R(-38, 38), z = R(-90, 12);
                if (Mathf.Abs(x - Mathf.Sin(z * 0.06f) * 5f) < 3f) continue;
                float s = R(0.4f, 2.4f);
                Place("Rock", "Rock" + (i % 6), "StoneRuins", G(x, z, -0.1f * s), Yaw(R(0, 360)), outdoor, s > 0.7f, s);
            }

            // erba secca
            var grassRoot = new GameObject("DeadGrass").transform; grassRoot.SetParent(outdoor);
            for (int i = 0; i < 320; i++)
            {
                float x = R(-36, 36), z = R(-88, 14);
                Place("Grass", "Grass" + (i % 2), "GrassTuft", G(x, z, -0.02f), Yaw(R(0, 360)), grassRoot, false, R(0.7f, 1.4f), false);
            }

            // ---- castello / sala del trono
            BuildCastle(castle, R);

            // confini invisibili
            var bounds = new GameObject("WorldBounds").transform; bounds.SetParent(_envRoot);
            AddBox(bounds, new Vector3(0, 10, -100), new Vector3(120, 40, 2));
            AddBox(bounds, new Vector3(-48, 10, -30), new Vector3(2, 40, 160));
            AddBox(bounds, new Vector3(48, 10, -30), new Vector3(2, 40, 160));

            // ---- riferimenti di livello
            var systems = new GameObject("Systems");
            var lv = systems.AddComponent<LevelRefs>();
            var spawn = new GameObject("PlayerSpawn_Bonfire").transform;
            spawn.SetParent(systems.transform); spawn.position = bonfire + new Vector3(0, 0.1f, 2.2f); spawn.rotation = Yaw(0);
            lv.playerSpawn = spawn;
            var enemyPts = new List<Transform>();
            foreach (var e in new[] { new Vector2(-3, -36), new Vector2(5, -24), new Vector2(-6, -12), new Vector2(4, -1), new Vector2(-2, 11) })
            {
                var t = new GameObject("EnemySpawn").transform;
                t.SetParent(systems.transform);
                t.position = G(e.x, e.y, 0.1f);
                t.rotation = Yaw(180 + R(-40, 40));
                enemyPts.Add(t);
            }
            lv.enemySpawns = enemyPts.ToArray();
            var bossSpawn = new GameObject("BossSpawn").transform; bossSpawn.SetParent(systems.transform);
            bossSpawn.position = new Vector3(0, 0.15f, 50f); bossSpawn.rotation = Yaw(180);
            lv.bossSpawn = bossSpawn;
            var arenaRoot = new GameObject("ArenaGate").transform; arenaRoot.SetParent(systems.transform);
            arenaRoot.position = new Vector3(0, 0.05f, 25.2f); arenaRoot.rotation = Yaw(0);
            lv.arenaRoot = arenaRoot;
            lv.arenaCenter = new Vector3(0, 6, 46.5f);
            lv.arenaSize = new Vector3(28, 16, 40);
            WireCastleRefs(lv, systems.transform, spawn);
            WireMirrorRefs(lv, systems.transform);

            systems.AddComponent<InputUpdater>();
            var sfx = systems.AddComponent<Sfx>();
            sfx.entries = _sfxEntries;
            sfx.slashPeakTime = 0.23f;   // measured peak of the sword-slash clip
            var music = systems.AddComponent<MusicPlayer>();
            music.bossTheme = AssetDatabase.LoadAssetAtPath<AudioClip>(BossMusic);
            music.dragonTheme = Snd("Assets/dragonbossfight.mp3", true);
            {
                // the Mirror's theme is cut into sections (seeks): kept compressed in memory, stereo
                const string MirrorMusic = "Assets/mirrorboss.mp3";
                if (AssetImporter.GetAtPath(MirrorMusic) is AudioImporter mai)
                {
                    var st = mai.defaultSampleSettings;
                    st.loadType = AudioClipLoadType.CompressedInMemory; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = 0.75f;
                    mai.defaultSampleSettings = st; mai.forceToMono = false; mai.loadInBackground = true;
                    mai.SaveAndReimport();
                }
                music.mirrorTheme = AssetDatabase.LoadAssetAtPath<AudioClip>(MirrorMusic);
                if (music.mirrorTheme == null) Debug.LogWarning("[DF] Missing " + MirrorMusic);
            }
            var mfx = systems.AddComponent<MirrorFx>();
            mfx.shardMesh = Meshes["ShardFx"]; mfx.shardMaterial = M["GlassShard"]; mfx.phantomMaterial = M["Ghost"];
            var fx = systems.AddComponent<Fx>();
            fx.additiveMaterial = M["ParticleAdd"];
            fx.alphaMaterial = M["ParticleAlpha"];
            fx.slashMaterial = M["SlashTrail"];
            fx.rippleMaterial = M["Ripple"];
            var sfx2 = systems.AddComponent<SpellFx>();
            sfx2.noteMaterial = M["SpellNote"]; sfx2.ringMaterial = M["SpellRing"]; sfx2.glowMaterial = M["SpellGlow"];
            systems.AddComponent<AtmosphereController>();

            // ---- camera (render a bassa risoluzione + VHS)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(systems.transform);
            camGo.transform.position = new Vector3(0, 8, -40);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 180f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.allowMSAA = false;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.volumeLayerMask = 1 | (1 << 2);   // Default (global grade) + Ignore Raycast (local grades, e.g. the terrace)
            camGo.AddComponent<AudioListener>();
            var sc = camGo.AddComponent<SoulsCamera>();
            sc.menuOrbitCenter = new Vector3(0, 6, -10);
            sc.menuOrbitRadius = 30f;
            var sp = camGo.AddComponent<ScreenPresenter>();
            sp.vhsMaterial = M["VHS_Screen"];

            // ---- UI
            var uiGo = new GameObject("GameUI");
            var ui = uiGo.AddComponent<GameUI>();
            ui.skin = skin;
            ui.spellIcons = _spellSprites;

            // ---- rete
            var netGo = new GameObject("NetworkManager");
            var nm = netGo.AddComponent<NetworkManager>();
            var utp = netGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = prefabs.player;
            nm.NetworkConfig.EnableSceneManagement = false;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(BuildNetworkPrefabList(prefabs));
            utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            var boot = netGo.AddComponent<NetworkBootstrap>();
            boot.playerPrefab = prefabs.player;
            boot.archerPrefab = prefabs.archer;
            boot.guardianPrefab = prefabs.guardian;
            boot.bossPrefab = prefabs.boss;
            boot.arenaPrefab = prefabs.arena;
            boot.lobbyPrefab = prefabs.lobby;
            boot.chestPrefab = prefabs.chest;
            boot.dragonChestPrefab = prefabs.dragonChest;
            boot.bardPrefab = prefabs.bard;
            boot.progressPrefab = prefabs.progress;
            boot.gaolerPrefab = prefabs.gaoler;
            boot.bowmanPrefab = prefabs.bowman;
            boot.wardenPrefab = prefabs.warden;
            boot.dragonPrefab = prefabs.dragon;
            boot.dragonArenaPrefab = prefabs.dragonArena;
            boot.radiantChestPrefab = prefabs.radiantChest;
            boot.wraithPrefab = prefabs.wraith;
            boot.revenantPrefab = prefabs.revenant;
            boot.mirrorPrefab = prefabs.mirror;
            boot.mirrorArenaPrefab = prefabs.mirrorArena;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); // riapre il file salvato (non "Untitled")
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => { s.enabled = false; return s; }).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static NetworkPrefabsList BuildNetworkPrefabList(Prefabs prefabs)
        {
            string path = $"{Gen}/Prefabs/DarkFantasyNetworkPrefabs.asset";
            if (AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path) != null) AssetDatabase.DeleteAsset(path);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (var go in new[] { prefabs.player, prefabs.archer, prefabs.bard, prefabs.guardian, prefabs.boss, prefabs.arena, prefabs.lobby, prefabs.chest, prefabs.dragonChest,
                                       prefabs.progress, prefabs.gaoler, prefabs.bowman, prefabs.warden, prefabs.dragon, prefabs.dragonArena,
                                       prefabs.radiantChest, prefabs.wraith, prefabs.revenant, prefabs.mirror, prefabs.mirrorArena })
                if (go != null) list.Add(new NetworkPrefab { Prefab = go });
            AssetDatabase.CreateAsset(list, path);
            return list;
        }

        static void BuildCastle(Transform castle, System.Func<float, float, float> R)
        {
            // facciata con portone (il cancello di nebbia sta a z = 25.2)
            Place("Facade", "CastleFacade", "StoneCastle", new Vector3(0, -0.5f, 25.2f), Quaternion.identity, castle);
            Place("GateArch", "GateArch", "StoneCastle", new Vector3(0, -0.5f, 25.2f), Quaternion.identity, castle);
            Place("Tower_L", "Tower", "StoneCastle", new Vector3(-16.5f, -0.5f, 25.2f), Yaw(22.5f), castle);
            Place("Tower_R", "Tower", "StoneCastle", new Vector3(16.5f, -0.5f, 25.2f), Yaw(22.5f), castle);
            // merlature spezzate sopra la facciata
            for (int i = 0; i < 4; i++)
                Place("Battlement", "RuinWall" + i, "StoneCastle", new Vector3(-10.5f + i * 7f, 16.4f, 25.2f), Yaw(R(-3, 3)), castle, false, 0.6f);

            // muri laterali con finestre (5 campate da 8 m)
            for (int i = 0; i < 5; i++)
            {
                float z = 30.2f + i * 8f;
                foreach (int side in new[] { -1, 1 })
                {
                    var rot = Yaw(90);
                    Place($"HallWall_{side}_{i}", "HallWallWindow", "StoneCastle", new Vector3(side * 15f, -0.5f, z), rot, castle);
                    Place($"Window_{side}_{i}", "WindowPane", "StainedGlass", new Vector3(side * 15f, -0.5f, z), rot, castle, false, 1f, false);
                }
            }
            // muro di fondo: vetrata in alto, la porta verso l'interno del castello sotto (si apre alla morte del re)
            BuildThroneDoorWall(castle);

            // pavimento a lastre (a tessere, per luci e culling)
            for (int x = -1; x <= 1; x++)
                for (int z = 0; z < 4; z++)
                    Place("Floor", "FloorTile", "Flagstones", new Vector3(x * 10f, 0.09f, 26.2f + z * 10f), Quaternion.identity, castle, true, 1f, false);

            // colonne interne con torce
            for (int i = 0; i < 5; i++)
            {
                float z = 30.2f + i * 8f;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = new Vector3(side * 9.5f, 0f, z);
                    Place("HallPillar", "PillarHall", "StoneCastle", p, Yaw(22.5f), castle);
                    if (i % 2 == 0 || i == 4)
                    {
                        var torch = Place("Torch", "WallTorch", "RottenWood", p + new Vector3(-side * 0.85f, 3.2f, 0), Yaw(side > 0 ? -90 : 90), castle, false);
                        Fx.CreateFlame(torch.transform, new Vector3(0, 0.45f, 0.38f), 0.9f, M["ParticleAdd"]);
                        AddLight(torch.transform, new Vector3(0, 0.6f, 0.6f), new Color(1f, 0.55f, 0.26f), 16f, 14f, false);
                    }
                }
            }

            // stendardi laceri tra le finestre
            for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 })
                    Place("Banner", "Banner", "Banner", new Vector3(side * 13.85f, 11.5f, 34.2f + i * 8f), Yaw(side > 0 ? -90 : 90), castle, false, 1f, false);

            // trono su pedana e bracieri
            Place("Dais", "Dais", "StoneCastle", new Vector3(0, 0, 58.6f), Quaternion.identity, castle);
            Place("Throne", "Throne", "RottenWood", new Vector3(0, 0.9f, 59.8f), Yaw(180), castle);
            foreach (int side in new[] { -1, 1 })
            {
                var br = Place("Brazier", "Brazier", "RustMetal", new Vector3(side * 5.2f, 0, 52.4f), Quaternion.identity, castle);
                Fx.CreateFlame(br.transform, new Vector3(0, 1.05f, 0), 2.2f, M["ParticleAdd"]);
                AddFireLoop(br.transform, 0.45f, 16f);
                AddLight(br.transform, new Vector3(0, 1.8f, 0), new Color(1f, 0.5f, 0.22f), 28f, 20f, side < 0);
            }

            // travi e tetto crollato (due pannelli mancanti lasciano entrare la luna)
            for (int i = 0; i < 11; i++)
            {
                float z = 27.2f + i * 4f;
                Place("Beam", "Beam", "RottenWood", new Vector3(0, 13.2f, z), Quaternion.Euler(R(-2, 2), 0, R(-2, 2)), castle, false);
                if (i == 3 || i == 4 || i == 8) continue;
                Place("Roof", "RoofPanel", "RottenWood", new Vector3(0, 13.7f, z), Quaternion.identity, castle, false);
            }
            // macerie del tetto sul pavimento
            for (int i = 0; i < 6; i++)
                Place("Rubble", "Rock" + (i % 6), "StoneCastle", new Vector3(R(-7, 7), 0f, R(38, 48)), Yaw(R(0, 360)), castle, true, R(0.4f, 1.1f));

            // luce lunare fredda che filtra dal tetto rotto
            var shaft = new GameObject("MoonShaft");
            shaft.transform.SetParent(castle);
            shaft.transform.position = new Vector3(0, 13f, 42f);
            shaft.transform.rotation = Quaternion.Euler(80, 0, 0);
            var sl = shaft.AddComponent<Light>();
            sl.type = LightType.Spot; sl.spotAngle = 60f; sl.range = 22f; sl.intensity = 70f; sl.color = new Color(0.6f, 0.68f, 0.82f);
            sl.shadows = LightShadows.Soft;

            // fill lights along the nave: the fight must be readable (no shadows, no flicker)
            for (int i = 0; i < 4; i++)
            {
                var fill = new GameObject("HallFill_" + i);
                fill.transform.SetParent(castle);
                fill.transform.position = new Vector3(0, 8.5f, 31f + i * 9f);
                var fl = fill.AddComponent<Light>();
                fl.type = LightType.Point; fl.range = 26f; fl.intensity = 110f; fl.color = new Color(0.95f, 0.78f, 0.6f);
                fl.shadows = LightShadows.None;
            }
            // cold rim from the rose window so the boss silhouette separates from the back wall
            var rim = new GameObject("RoseWindowRim");
            rim.transform.SetParent(castle);
            rim.transform.position = new Vector3(0, 9f, 65f);
            rim.transform.rotation = Quaternion.Euler(25, 180, 0);
            var rl = rim.AddComponent<Light>();
            rl.type = LightType.Spot; rl.spotAngle = 95f; rl.range = 40f; rl.intensity = 60f; rl.color = new Color(0.62f, 0.66f, 0.8f);
            rl.shadows = LightShadows.None;

            // ---- chapter 2: interior, prisons, stair, terrace
            BuildCastleInterior(castle);
            BuildTerrace(castle, R);
            BuildBeyond(castle);
        }

        static void AddLight(Transform parent, Vector3 localPos, Color c, float intensity, float range, bool shadows)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            go.AddComponent<FlickerLight>();
        }

        static void AddBox(Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("Bound");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
        }

        static VolumeProfile BuildVolumeProfile()
        {
            string path = $"{Gen}/Rendering/DarkFantasyVolume.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.45f); ca.contrast.Override(16f); ca.saturation.Override(-38f); ca.colorFilter.Override(new Color(0.93f, 0.95f, 0.88f));
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.36f, 0.42f, 0.33f)); split.highlights.Override(new Color(0.6f, 0.52f, 0.42f)); split.balance.Override(-15f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.28f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.02f, 0.02f, 0.02f));
            var grain = profile.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(0.3f);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.4f); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.6f);
            foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
