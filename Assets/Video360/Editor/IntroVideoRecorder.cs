using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

// Records the 360 intro (equirectangular MP4) in <project>/Recordings using Unity Recorder.
// Each clip is one Play Mode session, queued so several can be recorded in a row.
[InitializeOnLoad]
public static class IntroVideoRecorder
{
    const string ScenePath = "Assets/Video360/Video360.unity";
    const string QueueKey = "IntroVideoRecorder.Queue";
    // 2:1 equirectangular; 4096x2048 is the largest size H.264 accepts.
    const int FrameRate = 30, Width = 4096, Height = 2048, CubeMapSize = 2048, WarmUpFrames = 30;

    static RecorderController controller;
    static string[] clip;
    static int warmUp;

    static IntroVideoRecorder()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // Same intro as Sprint 1 (version with the bite), 39 s, for the main menu.
    [MenuItem("Tools/Video 360/Record 360 Video")]
    public static void Record360()
    {
        Enqueue(Clip("Intro360", 0f, IntroDirector.ExtendedDuration, true, 0, true));
    }

    static string Clip(string name, float from, float to, bool fades, int soloShot, bool extended)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join("|", name, from.ToString(inv), to.ToString(inv), fades ? "1" : "0", soloShot.ToString(), extended ? "1" : "0");
    }

    static List<string> Queue
    {
        get => SessionState.GetString(QueueKey, "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        set => SessionState.SetString(QueueKey, string.Join(";", value));
    }

    static void Enqueue(params string[] clips)
    {
        var queue = Queue;
        queue.AddRange(clips);
        Queue = queue;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Queue = new List<string>(); return; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && Queue.Count > 0)
        {
            var queue = Queue;
            clip = queue[0].Split('|');
            queue.RemoveAt(0);
            Queue = queue;
            warmUp = WarmUpFrames;
            EditorApplication.update += Tick;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && Queue.Count > 0)
        {
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        var director = Object.FindFirstObjectByType<IntroDirector>();
        if (!director) { Debug.LogError("IntroDirector not found in the scene."); Stop(); EditorApplication.ExitPlaymode(); return; }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float from = float.Parse(clip[1], inv), to = float.Parse(clip[2], inv);

        if (controller == null)
        {
            // Hold the first frame while water and exposure settle.
            director.autoPlay = false;
            director.fades = clip[3] == "1";
            director.soloShot = int.Parse(clip[4]);
            director.extended = clip[5] == "1";
            director.Play(from, from);
            if (--warmUp > 0) return;

            int frames = Mathf.RoundToInt((to - from) * FrameRate);
            controller = new RecorderController(Settings(clip[0], frames));
            controller.PrepareRecording();
            controller.StartRecording();
            director.Play(from, to);
            Debug.Log("Recording " + clip[0] + " (" + frames + " frames)");
            return;
        }

        if (controller.IsRecording()) return;
        Debug.Log("Finished " + clip[0]);
        Stop();
        EditorApplication.ExitPlaymode();
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        if (controller != null && controller.IsRecording()) controller.StopRecording();
        controller = null;
    }

    static RecorderControllerSettings Settings(string name, int frames)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings");
        Directory.CreateDirectory(folder);

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = name;
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
        };
        movie.CaptureAudio = false;
        movie.ImageInputSettings = new Camera360InputSettings
        {
            Source = ImageSource.MainCamera,
            OutputWidth = Width,
            OutputHeight = Height,
            MapSize = CubeMapSize,
            RenderStereo = false
        };
        movie.OutputFile = Path.Combine(folder, name);

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToFrameInterval(0, frames - 1);
        settings.FrameRate = FrameRate;
        settings.CapFrameRate = false;
        return settings;
    }
}
