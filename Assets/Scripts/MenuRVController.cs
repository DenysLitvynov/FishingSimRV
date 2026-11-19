using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Reuses the Menu scene, its two buttons, and its existing XR Player.
public sealed class MenuRVController : MonoBehaviour
{
    [Header("Menu existente")]
    [SerializeField] Canvas menuCanvas;
    [SerializeField] Button videoButton;
    [SerializeField] Button gameButton;
    [SerializeField] TMP_Text instructionText;
    [SerializeField] TMP_FontAsset font;

    [Header("Destinos")]
    [SerializeField] VideoClip introClip;
    [SerializeField] Material panoramicMaterial;
    [SerializeField] string gameSceneName = "Juego";

    [Header("Panel en el mundo")]
    [SerializeField, Range(1f, 2f)] float panelDistance = 1.7f;

    const int UILayer = 5;
    const string DefaultInstruction = "Apunta con el rayo y pulsa el gatillo";
    readonly Dictionary<GameObject, int> rigLayers = new Dictionary<GameObject, int>();
    readonly List<LocomotionProvider> menuLocomotion = new List<LocomotionProvider>();

    XROrigin origin;
    Camera headCamera;
    CanvasGroup menuGroup;
    Canvas controls;
    TMP_Text progressText;
    TMP_Text pauseLabel;
    Button pauseButton;
    Button restartButton;
    VideoPlayer player;
    RenderTexture videoTexture;
    Material videoMaterial;
    Material savedSkybox;
    CameraClearFlags savedClearFlags;
    int savedCullingMask;
    bool videoMode;
    bool loadingGame;
    bool videoFinished;
    float prepareDeadline;
    float nextProgressUpdate;

    void Awake()
    {
        if (!menuCanvas) menuCanvas = GetComponent<Canvas>();
        origin = FindFirstObjectByType<XROrigin>();
        headCamera = origin && origin.Camera ? origin.Camera : Camera.main;
        menuGroup = menuCanvas.GetComponent<CanvasGroup>();
        if (!menuGroup) menuGroup = menuCanvas.gameObject.AddComponent<CanvasGroup>();
        menuCanvas.renderMode = RenderMode.WorldSpace;
        menuCanvas.worldCamera = headCamera;

        // Menu navigation must not teleport or turn the player away from the panel.
        // Juego loads its own XR Player with its original locomotion configuration.
        if (origin)
        {
            foreach (var provider in origin.GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (!provider.enabled) continue;
                menuLocomotion.Add(provider);
                provider.enabled = false;
            }
        }
        BuildVideoControls();
    }

    IEnumerator Start()
    {
        PlacePanel(menuCanvas, 0f);
        // Allow the XR camera to receive its first tracked pose, then leave the
        // panel stationary in the world. It does not follow head rotation.
        yield return new WaitForSecondsRealtime(0.35f);
        if (!videoMode && !loadingGame) PlacePanel(menuCanvas, 0f);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame)
                PlacePanel(videoMode ? controls : menuCanvas, videoMode ? -0.46f : 0f);
            if (videoMode && keyboard.escapeKey.wasPressedThisFrame) ReturnToMenu();
        }

        if (!videoMode || !player) return;
        if (!player.isPrepared && Time.unscaledTime > prepareDeadline)
        {
            FailVideo("El vídeo no se ha podido preparar. Vuelve a intentarlo.");
            return;
        }
        if (!player.isPrepared || Time.unscaledTime < nextProgressUpdate) return;
        nextProgressUpdate = Time.unscaledTime + 0.2f;
        progressText.text = (videoFinished ? "Fin del tráiler  ·  " : "")
            + FormatTime(player.time) + " / " + FormatTime(player.length);
    }

    // These public methods are connected to the two original Buttons in Menu.unity.
    public void PlayVideo360()
    {
        if (videoMode || loadingGame) return;
        if (!headCamera || !introClip || !panoramicMaterial)
        {
            SetInstruction("Falta la cámara, el vídeo o el material panorámico.");
            return;
        }
        if (!panoramicMaterial.shader || !panoramicMaterial.shader.isSupported)
        {
            SetInstruction("El material del vídeo no es compatible con este equipo.");
            return;
        }

        EnsurePlayer();
        videoFinished = false;
        savedSkybox = RenderSettings.skybox;
        savedClearFlags = headCamera.clearFlags;
        savedCullingMask = headCamera.cullingMask;
        videoMode = true;

        // Keep controller models and rays visible while hiding the lake geometry.
        rigLayers.Clear();
        if (origin)
        {
            foreach (var renderer in origin.GetComponentsInChildren<Renderer>(true))
            {
                var rigObject = renderer.gameObject;
                if (rigLayers.ContainsKey(rigObject)) continue;
                rigLayers.Add(rigObject, rigObject.layer);
                rigObject.layer = UILayer;
            }
        }
        headCamera.cullingMask = 1 << UILayer;
        headCamera.clearFlags = CameraClearFlags.Skybox;
        videoMaterial.SetFloat("_Rotation", headCamera.transform.eulerAngles.y);
        RenderSettings.skybox = videoMaterial;
        SetMenuVisible(false);
        controls.gameObject.SetActive(true);
        PlacePanel(controls, -0.46f);
        progressText.text = "Preparando vídeo 360º…";
        pauseLabel.text = "Pausar";
        pauseButton.interactable = false;
        restartButton.interactable = false;
        prepareDeadline = Time.unscaledTime + 30f;
        nextProgressUpdate = 0f;
        player.Prepare();
    }

    public void OpenGame()
    {
        if (loadingGame || videoMode) return;
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SetInstruction("Añade la escena Juego a Build Profiles > Scene List.");
            return;
        }
        loadingGame = true;
        videoButton.interactable = false;
        gameButton.interactable = false;
        SetInstruction("Cargando juego…");
        StartCoroutine(LoadGame());
    }

    IEnumerator LoadGame()
    {
        // Show the loading message before starting the scene transition.
        yield return null;
        SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);
    }

    public void ReturnToMenu()
    {
        if (!videoMode) return;
        // Clear the flag before Stop, so late decoder events cannot reopen playback.
        videoMode = false;
        if (player) player.Stop();
        RestoreView();
        controls.gameObject.SetActive(false);
        ClearVideoTexture();
        SetMenuVisible(true);
        PlacePanel(menuCanvas, 0f);
        SetInstruction(DefaultInstruction);
    }

    public void TogglePause()
    {
        if (!videoMode || !player || !player.isPrepared) return;
        if (videoFinished) { RestartVideo(); return; }
        if (player.isPlaying)
        {
            player.Pause();
            pauseLabel.text = "Reanudar";
        }
        else
        {
            player.Play();
            pauseLabel.text = "Pausar";
        }
    }

    public void RestartVideo()
    {
        if (!videoMode || !player || !player.isPrepared) return;
        videoFinished = false;
        player.time = 0;
        player.Play();
        pauseLabel.text = "Pausar";
    }

    void EnsurePlayer()
    {
        if (player) return;
        videoTexture = new RenderTexture((int)introClip.width, (int)introClip.height,
            0, RenderTextureFormat.ARGB32)
        {
            name = "Intro360 (runtime)",
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            useMipMap = false,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            antiAliasing = 1
        };
        videoTexture.Create();
        ClearVideoTexture();
        videoMaterial = new Material(panoramicMaterial);
        videoMaterial.SetTexture("_MainTex", videoTexture);

        var playerObject = new GameObject("Reproductor Intro360");
        playerObject.transform.SetParent(transform, false);
        player = playerObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.source = VideoSource.VideoClip;
        player.clip = introClip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = videoTexture;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.isLooping = false;
        player.audioOutputMode = VideoAudioOutputMode.None;
        if (introClip.audioTrackCount > 0)
        {
            var audio = playerObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audio);
        }
        player.prepareCompleted += OnVideoPrepared;
        player.loopPointReached += OnVideoFinished;
        player.errorReceived += OnVideoError;
    }

    void OnVideoPrepared(VideoPlayer source)
    {
        if (!videoMode) return;
        pauseButton.interactable = true;
        restartButton.interactable = true;
        source.Play();
    }

    void OnVideoFinished(VideoPlayer source)
    {
        if (!videoMode) return;
        videoFinished = true;
        pauseLabel.text = "Repetir";
    }

    void OnVideoError(VideoPlayer source, string message)
    {
        if (videoMode) FailVideo("No se pudo reproducir el vídeo. Revisa la consola.");
        Debug.LogError("Intro360: " + message, this);
    }

    void FailVideo(string message)
    {
        ReturnToMenu();
        SetInstruction(message);
    }

    void SetMenuVisible(bool visible)
    {
        menuGroup.alpha = visible ? 1f : 0f;
        menuGroup.interactable = visible;
        menuGroup.blocksRaycasts = visible;
    }

    void PlacePanel(Canvas canvas, float heightOffset)
    {
        if (!headCamera || !canvas) return;
        var forward = Vector3.ProjectOnPlane(headCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
            forward = origin ? origin.transform.forward : Vector3.forward;
        forward.y = 0f;
        forward.Normalize();
        canvas.transform.SetPositionAndRotation(
            headCamera.transform.position + forward * panelDistance + Vector3.up * heightOffset,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    void RestoreView()
    {
        RenderSettings.skybox = savedSkybox;
        if (headCamera)
        {
            headCamera.clearFlags = savedClearFlags;
            headCamera.cullingMask = savedCullingMask;
        }
        foreach (var entry in rigLayers)
            if (entry.Key) entry.Key.layer = entry.Value;
        rigLayers.Clear();
    }

    void ClearVideoTexture()
    {
        if (!videoTexture) return;
        var previous = RenderTexture.active;
        RenderTexture.active = videoTexture;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = previous;
    }

    void SetInstruction(string text)
    {
        if (instructionText) instructionText.text = text;
    }

    static string FormatTime(double seconds)
    {
        int whole = Mathf.Max(0, (int)seconds);
        return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
    }

    void BuildVideoControls()
    {
        var root = new GameObject("Controles Video360", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        root.layer = UILayer;
        controls = root.GetComponent<Canvas>();
        controls.renderMode = RenderMode.WorldSpace;
        controls.worldCamera = headCamera;
        controls.sortingOrder = 10;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 192f);
        root.transform.localScale = Vector3.one * 0.002f;
        root.GetComponent<TrackedDeviceGraphicRaycaster>().ignoreReversedGraphics = true;
        root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        var background = root.AddComponent<Image>();
        background.color = new Color(0.025f, 0.06f, 0.09f, 0.98f);
        background.raycastTarget = false;
        progressText = MakeText(root.transform, "Estado", "", new Vector2(0f, 61f),
            new Vector2(760f, 42f), 28f);
        pauseButton = MakeButton(root.transform, "Pausar", -255f, TogglePause, out pauseLabel);
        TMP_Text unused;
        restartButton = MakeButton(root.transform, "Reiniciar", 0f, RestartVideo, out unused);
        MakeButton(root.transform, "Volver al menú", 255f, ReturnToMenu, out unused);
        MakeText(root.transform, "Ayuda", "Mira a tu alrededor · Selecciona con rayo y gatillo",
            new Vector2(0f, -69f), new Vector2(760f, 30f), 23f);
        root.SetActive(false);
    }

    Button MakeButton(Transform parent, string title, float x,
        UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        var root = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = UILayer;
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = new Vector2(x, -6f);
        rect.sizeDelta = new Vector2(238f, 74f);
        root.GetComponent<Image>().color = Color.white;
        var button = root.GetComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        var colors = button.colors;
        colors.normalColor = new Color(0.04f, 0.29f, 0.38f);
        colors.highlightedColor = new Color(0.04f, 0.43f, 0.48f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = new Color(0.02f, 0.19f, 0.25f);
        colors.disabledColor = new Color(0.15f, 0.19f, 0.22f);
        button.colors = colors;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.onClick.AddListener(action);
        label = MakeText(root.transform, "Texto", title, Vector2.zero, new Vector2(226f, 70f), 29f);
        return button;
    }

    TMP_Text MakeText(Transform parent, string name, string text, Vector2 position,
        Vector2 size, float fontSize)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = UILayer;
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var label = root.GetComponent<TextMeshProUGUI>();
        label.font = font ? font : TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    void OnDestroy()
    {
        if (player)
        {
            player.prepareCompleted -= OnVideoPrepared;
            player.loopPointReached -= OnVideoFinished;
            player.errorReceived -= OnVideoError;
            player.Stop();
        }
        if (videoMode) RestoreView();
        foreach (var provider in menuLocomotion)
            if (provider) provider.enabled = true;
        if (controls) Destroy(controls.gameObject);
        if (videoTexture) { videoTexture.Release(); Destroy(videoTexture); }
        if (videoMaterial) Destroy(videoMaterial);
    }
}
