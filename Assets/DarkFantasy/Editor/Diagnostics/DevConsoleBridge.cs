using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Strumento di sviluppo (solo Editor, assembly indipendente così compila anche quando il
    /// resto del progetto ha errori).
    ///  - Scrive errori di compilazione ed errori/eccezioni della Console in Logs/DarkFantasy_Console.log
    ///  - Esegue comandi scritti in Logs/df_command.txt (una riga per comando):
    ///      menu:&lt;percorso menu&gt;   esegue una voce di menu (es. menu:Dark Fantasy/Costruisci Tutto)
    ///      play / stop          entra/esce dal Play Mode
    ///      shot                 salva uno screenshot della Game view in Logs/df_shot.png
    ///      refresh              AssetDatabase.Refresh()
    /// Utile per test automatizzati / CI. Può essere eliminato senza conseguenze.
    /// </summary>
    [InitializeOnLoad]
    public static class DevConsoleBridge
    {
        static readonly string LogsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        static readonly string LogPath = Path.Combine(LogsDir, "DarkFantasy_Console.log");
        static readonly string CmdPath = Path.Combine(LogsDir, "df_command.txt");
        static double _nextPoll;

        static DevConsoleBridge()
        {
            Directory.CreateDirectory(LogsDir);
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationStarted -= OnCompileStarted;
            CompilationPipeline.compilationStarted += OnCompileStarted;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Write($"[bridge] domain reload {DateTime.Now:HH:mm:ss} unity={Application.unityVersion}");
        }

        static void OnPlayMode(PlayModeStateChange s) => Write($"[playmode] {s}");
        static void OnCompileStarted(object o) => Write($"[compile] started {DateTime.Now:HH:mm:ss}");

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            int errors = 0;
            var sb = new StringBuilder();
            foreach (var m in messages)
            {
                if (m.type == CompilerMessageType.Error) errors++;
                if (m.type == CompilerMessageType.Error || (m.type == CompilerMessageType.Warning && m.file.Contains("DarkFantasy")))
                    sb.AppendLine($"  [{m.type}] {m.file}({m.line},{m.column}): {m.message}");
            }
            Write($"[compile] {Path.GetFileName(assembly)} errors={errors}\n{sb}");
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log && !condition.StartsWith("[DF]")) return;
            string st = type == LogType.Exception || type == LogType.Error ? "\n    " + (stackTrace ?? "").Replace("\n", "\n    ") : "";
            Write($"[{type}] {condition}{st}");
        }

        /// <summary>dump:NomeOggetto,NomeFiglio1,NomeFiglio2 → posizioni/scale/bounds nel log.</summary>
        static void Dump(string arg)
        {
            var parts = arg.Split(',');
            var root = GameObject.Find(parts[0]);
            if (root == null) { Write("[dump] non trovato: " + parts[0]); return; }
            var sb = new StringBuilder();
            sb.AppendLine($"[dump] {root.name} pos={root.transform.position} rot={root.transform.eulerAngles} fwd={root.transform.forward}");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool want = parts.Length == 1 && t.parent == root.transform;
                for (int i = 1; i < parts.Length; i++) if (t.name == parts[i]) want = true;
                if (!want) continue;
                var r = t.GetComponent<Renderer>();
                sb.AppendLine($"  {t.name} pos={t.position:F3} up={t.up:F2} fwd={t.forward:F2} lossy={t.lossyScale:F3} active={t.gameObject.activeInHierarchy}" + (r ? $" bounds={r.bounds.center:F2}/{r.bounds.size:F2}" : ""));
            }
            Write(sb.ToString());
        }

        /// <summary>render:px,py,pz,tx,ty,tz[,fov] → Logs/df_render.png (960x540) da una camera temporanea.</summary>
        static void Render(string arg)
        {
            var v = Array.ConvertAll(arg.Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            var go = new GameObject("DF_RenderCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.position = new Vector3(v[0], v[1], v[2]);
            cam.transform.LookAt(new Vector3(v[3], v[4], v[5]));
            cam.fieldOfView = v.Length > 6 ? v[6] : 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(LogsDir, "df_render.png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
            Write("[render] ok " + arg);
        }

        static readonly object _lock = new object();
        static void Write(string line)
        {
            try { lock (_lock) File.AppendAllText(LogPath, line + "\n"); } catch { /* ignora */ }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(CmdPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(CmdPath); File.Delete(CmdPath); } catch { return; }
            foreach (var raw in lines)
            {
                var cmd = raw.Trim();
                if (cmd.Length == 0) continue;
                Write($"[cmd] {cmd}");
                try
                {
                    if (cmd.StartsWith("menu:")) Write($"[cmd] result={EditorApplication.ExecuteMenuItem(cmd.Substring(5))}");
                    else if (cmd == "play") EditorApplication.isPlaying = true;
                    else if (cmd == "stop") EditorApplication.isPlaying = false;
                    else if (cmd == "refresh") AssetDatabase.Refresh();
                    else if (cmd == "shot") ScreenCapture.CaptureScreenshot(Path.Combine(LogsDir, "df_shot.png"));
                    else if (cmd == "clear") File.WriteAllText(LogPath, "");
                    else if (cmd.StartsWith("launch:"))
                    {
                        // launch:<args>  → runs the last Windows test build (Builds/Windows/DarkFantasy.exe)
                        string exe = Path.GetFullPath("Builds/Windows/DarkFantasy.exe");
                        if (!File.Exists(exe)) Write("[launch] missing " + exe);
                        else { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, cmd.Substring(7)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) }); Write("[launch] ok " + cmd.Substring(7)); }
                    }
                    else if (cmd.StartsWith("kill"))
                    {
                        foreach (var p in System.Diagnostics.Process.GetProcessesByName("DarkFantasy")) { try { p.Kill(); } catch { } }
                        Write("[kill] ok");
                    }
                    else if (cmd.StartsWith("dump:")) Dump(cmd.Substring(5));
                    else if (cmd.StartsWith("render:")) Render(cmd.Substring(7));
                    else if (cmd.StartsWith("msg:"))
                    {
                        // msg:NomeOggetto,Metodo,argomento  → SendMessage (solo Play Mode)
                        var a = cmd.Substring(4).Split(new[] { ',' }, 3);
                        var go = GameObject.Find(a[0]);
                        if (go == null) Write("[msg] non trovato: " + a[0]);
                        else { if (a.Length > 2) go.SendMessage(a[1], a[2]); else go.SendMessage(a[1]); Write("[msg] ok"); }
                    }
                }
                catch (Exception e) { Write($"[cmd] failed: {e}"); }
            }
        }
    }
}
