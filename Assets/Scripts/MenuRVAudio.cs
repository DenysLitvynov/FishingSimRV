using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns menu music, lake ambience and the original Serene Lake effects.
// Survives the transition to Juego, so a scene load does not cut the click sound.
public sealed class MenuRVAudio : MonoBehaviour
{
    public static readonly string[] ClipNames =
    {
        "ambient_lake_loop", "boat_motor_start_short", "boat_wood_creak",
        "instructions_open_soft", "marker_found_chime", "minigame_ding_splash",
        "movement_water_swish", "trailer_reel_splash", "ui_back_bubble",
        "ui_click_water", "ui_open_water_whoosh",
        "SereneLake_Menu_Theme_Animated", "SereneLake_Menu_Theme",
        "SereneLake_Anzuelos_Selection_Arcade", "SereneLake_Seleccion_Anzuelos_Limpia",
        "SereneLake_Seleccion_Anzuelos", "SereneLake_Boton_Referencia",
        "SereneLake_Boton_Chapuzon", "SereneLake_Boton_Pesca", "ui_boton_serenelake",
        "ui_click_menu_original"
    };

    static readonly string[] MenuPlaylist =
        { "SereneLake_Menu_Theme_Animated" };
    static readonly string[] GamePlaylist =
        { "SereneLake_Seleccion_Anzuelos_Limpia", "SereneLake_Anzuelos_Selection_Arcade", "SereneLake_Seleccion_Anzuelos" };
    static readonly string[] ClickVariants =
        { "ui_click_water", "SereneLake_Boton_Referencia", "SereneLake_Boton_Chapuzon", "SereneLake_Boton_Pesca" };
    static readonly float[] CueTimes = { 0.6f, 3.2f, 12.6f, 19.7f, 26.18f, 27.4f, 36.6f };
    static readonly string[] CueNames =
    {
        "boat_wood_creak", "movement_water_swish", "boat_motor_start_short",
        "boat_wood_creak", "trailer_reel_splash", "SereneLake_Boton_Chapuzon", "SereneLake_Boton_Pesca"
    };

    static MenuRVAudio instance;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    AudioSource music;
    AudioSource ambient;
    AudioSource ui;
    AudioSource effects;
    string[] playlist;
    int musicIndex;
    int clickIndex;
    int nextCue;
    bool videoActive;
    bool videoPaused;
    bool inGame;
    float nextHoverTime;
    float nextMovementTime;
    double previousVideoTime;
    XROrigin gameOrigin;
    Vector3 previousOriginPosition;
    Coroutine audition;
    bool auditioning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartForProjectScenes()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Menu" || scene == "Juego") Ensure();
    }

    static MenuRVAudio Ensure()
    {
        if (instance) return instance;
        instance = FindFirstObjectByType<MenuRVAudio>();
        if (!instance) instance = new GameObject("Audio Serene Lake RV").AddComponent<MenuRVAudio>();
        return instance;
    }

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        music = MakeSource("Música", 0.24f, false);
        ambient = MakeSource("Ambiente", 0.16f, true);
        ui = MakeSource("Interfaz", 0.70f, false);
        effects = MakeSource("Efectos", 0.70f, false);
        foreach (string name in ClipNames)
        {
            var clip = Resources.Load<AudioClip>("SereneLakeAudio/" + name);
            if (clip) clips.Add(name, clip);
            else Debug.LogError("Falta el audio SereneLakeAudio/" + name, this);
        }
        ambient.clip = Get("ambient_lake_loop");
        if (ambient.clip) ambient.Play();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ConfigureScene(SceneManager.GetActiveScene().name);
    }

    AudioSource MakeSource(string name, float volume, bool loop)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = volume;
        source.loop = loop;
        return source;
    }

    AudioClip Get(string name)
    {
        AudioClip clip;
        return clips.TryGetValue(name, out clip) ? clip : null;
    }

    void Play(AudioSource source, string name, float volume)
    {
        if (auditioning) return;
        var clip = Get(name);
        if (clip) source.PlayOneShot(clip, volume);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single || scene.name == "Menu" || scene.name == "Juego")
            ConfigureScene(scene.name);
    }

    void ConfigureScene(string scene)
    {
        if (audition != null)
        {
            StopCoroutine(audition);
            audition = null;
            auditioning = false;
            ui.Stop();
        }
        videoActive = false;
        videoPaused = false;
        effects.Stop();
        inGame = scene == "Juego";
        playlist = scene == "Menu" ? MenuPlaylist : inGame ? GamePlaylist : null;
        musicIndex = 0;
        music.Stop();
        // Keep the first menu song repeating at its original speed.
        music.loop = scene == "Menu";
        music.pitch = 1f;
        ambient.volume = 0.16f;
        if (playlist == null) { ambient.Stop(); return; }
        if (ambient.clip && !ambient.isPlaying) ambient.Play();
        PlayNextTrack();
        if (inGame)
        {
            gameOrigin = FindFirstObjectByType<XROrigin>();
            if (gameOrigin) previousOriginPosition = gameOrigin.transform.position;
            Play(effects, "boat_wood_creak", 0.65f);
        }
    }

    void PlayNextTrack()
    {
        if (playlist == null || playlist.Length == 0) return;
        for (int i = 0; i < playlist.Length; i++)
        {
            var clip = Get(playlist[musicIndex]);
            musicIndex = (musicIndex + 1) % playlist.Length;
            if (!clip) continue;
            music.clip = clip;
            music.Play();
            return;
        }
    }

    void Update()
    {
        if (auditioning) return;
        if (!videoActive && playlist != null && !music.isPlaying) PlayNextTrack();
        if (!inGame || !gameOrigin || Time.unscaledTime < nextMovementTime) return;
        var position = gameOrigin.transform.position;
        if (Vector3.Distance(position, previousOriginPosition) < 0.25f) return;
        previousOriginPosition = position;
        nextMovementTime = Time.unscaledTime + 0.45f;
        Play(effects, "movement_water_swish", 0.55f);
    }

    public static void MenuArrived()
    {
        // The reference menu has no additional arrival sound.
        Ensure();
    }

    public static void Hover()
    {
        var audio = Ensure();
        // In the reference menu only a completed button click makes a sound.
        if (!audio.inGame && !audio.videoActive) return;
        if (Time.unscaledTime < audio.nextHoverTime) return;
        audio.nextHoverTime = Time.unscaledTime + 0.15f;
        audio.Play(audio.ui, "ui_boton_serenelake", 0.22f);
    }

    public static void Click()
    {
        var audio = Ensure();
        audio.Play(audio.ui, ClickVariants[audio.clickIndex++ % ClickVariants.Length], 0.65f);
    }

    // Exact original clip identified in the supplied recording. One sound per
    // selection; source volume 0.70 * scale 0.32 matches its recorded level.
    public static void MenuButtonClick() { var a = Ensure(); a.Play(a.ui, "ui_click_menu_original", 0.32f); }
    public static void GameSelected() { MenuButtonClick(); }
    public static void TrailerSelected() { MenuButtonClick(); }
    public static void InstructionsSelected() { MenuButtonClick(); }
    public static void Back() { var a = Ensure(); a.Play(a.ui, "ui_back_bubble", 0.70f); }

    public static void SetTrailer(bool active, bool hasEmbeddedAudio = false)
    {
        var audio = Ensure();
        audio.videoActive = active;
        audio.videoPaused = false;
        audio.effects.Stop();
        audio.ambient.volume = active && hasEmbeddedAudio ? 0.04f : 0.16f;
        if (active) audio.music.Pause();
        else audio.music.UnPause();
        SyncVideoTime(0);
    }

    public static void PauseVideoEffects(bool paused)
    {
        var audio = Ensure();
        audio.videoPaused = paused;
        if (paused) audio.effects.Pause(); else audio.effects.UnPause();
    }

    public static void SyncVideoTime(double time)
    {
        var audio = Ensure();
        audio.effects.Stop();
        audio.previousVideoTime = time;
        audio.nextCue = 0;
        while (audio.nextCue < CueTimes.Length && CueTimes[audio.nextCue] <= time)
            audio.nextCue++;
    }

    public static void TickVideo(double time, bool playing)
    {
        var audio = Ensure();
        if (!audio.videoActive || audio.videoPaused || !playing) return;
        if (time < audio.previousVideoTime - 0.2) SyncVideoTime(time);
        while (audio.nextCue < CueTimes.Length && time >= CueTimes[audio.nextCue])
        {
            audio.Play(audio.effects, CueNames[audio.nextCue], 0.70f);
            audio.nextCue++;
        }
        audio.previousVideoTime = time;
    }

    public static void PreviewAllSounds()
    {
        var audio = Ensure();
        if (audio.videoActive || SceneManager.GetActiveScene().name != "Menu")
        {
            Debug.LogWarning("Vuelve al menú principal antes de probar todos los sonidos.");
            return;
        }
        if (audio.audition != null) return;
        audio.audition = audio.StartCoroutine(audio.AuditionAll());
    }

    IEnumerator AuditionAll()
    {
        auditioning = true;
        music.Pause();
        ambient.Pause();
        effects.Pause();
        foreach (string name in ClipNames)
        {
            var clip = Get(name);
            if (!clip) continue;
            ui.Stop();
            Debug.Log("Probando audio: " + name, this);
            ui.PlayOneShot(clip, 0.65f);
            yield return new WaitForSecondsRealtime(Mathf.Min(clip.length, 3f) + 0.15f);
        }
        ui.Stop();
        auditioning = false;
        ambient.UnPause();
        music.UnPause();
        effects.UnPause();
        audition = null;
        Debug.Log("Terminada la prueba de los " + ClipNames.Length + " audios de Serene Lake RV.", this);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }
}
