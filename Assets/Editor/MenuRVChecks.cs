using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.EventSystems;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Checks the actual Unity imports and serialized scene, without editing the scene.
public static class MenuRVChecks
{
    [MenuItem("Tools/Serene Lake RV/Comprobar menú y audios")]
    public static void ValidateMenuAndAudio()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Sal de Play antes de comprobar los archivos del menú.");
            return;
        }
        const string path = "Assets/Scenes/Menu.unity";
        var previousScene = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        int errors = 0;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var canvas = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                .FirstOrDefault(item => item.name == "Menu_RV");
            if (!canvas) throw new InvalidOperationException("No se encuentra el Canvas Menu_RV.");
            if (canvas.renderMode != RenderMode.WorldSpace) Report("El Canvas no está en World Space.", ref errors);
            ValidateXR(scene, canvas, ref errors);
            var buttons = canvas.GetComponentsInChildren<Button>(false);
            if (buttons.Length != 3) Report("El menú debe tener exactamente tres botones activos; hay " + buttons.Length + ".", ref errors);
            string[] sprites = { "btn_minijuego", "btn_ver_trailer", "btn_instrucciones" };
            string[] methods = { "OpenGame", "PlayVideo360", "OpenInstructions" };
            for (int i = 0; i < sprites.Length; i++)
            {
                var button = buttons.FirstOrDefault(item => item.image && item.image.sprite && item.image.sprite.name == sprites[i]);
                if (!button) { Report("Falta el botón original " + sprites[i], ref errors); continue; }
                if (button.onClick.GetPersistentEventCount() != 1
                    || button.onClick.GetPersistentMethodName(0) != methods[i]
                    || !(button.onClick.GetPersistentTarget(0) is MenuRVController))
                    Report("Acción incorrecta en " + button.name, ref errors);
            }
            var controller = canvas.GetComponent<MenuRVController>();
            if (!controller) Report("Falta MenuRVController.", ref errors);
            else
            {
                var serialized = new SerializedObject(controller);
                foreach (string field in new[] { "menuCanvas", "videoButton", "gameButton", "font", "instructionsButton", "instructionsArtwork", "gameButtonArtwork",
                    "trailerButtonArtwork", "instructionsButtonArtwork", "backButtonArtwork", "introClip", "panoramicMaterial" })
                {
                    var property = serialized.FindProperty(field);
                    if (property == null || property.objectReferenceValue == null)
                        Report("Referencia sin asignar: " + field, ref errors);
                }
            }
            if (controller)
            {
                var serialized = new SerializedObject(controller);
                var clip = serialized.FindProperty("introClip").objectReferenceValue as VideoClip;
                if (clip && (clip.width == 0 || clip.height == 0 || clip.length <= 0))
                    Report("El vídeo 360 no se ha importado correctamente.", ref errors);
                var material = serialized.FindProperty("panoramicMaterial").objectReferenceValue as Material;
                if (material && (!material.shader || !material.shader.isSupported))
                    Report("El shader del vídeo 360 no es compatible con este equipo.", ref errors);
            }
            foreach (string name in MenuRVAudio.ClipNames)
            {
                var clip = Resources.Load<AudioClip>("SereneLakeAudio/" + name);
                if (!clip || clip.samples == 0 || clip.length <= 0 || clip.channels == 0)
                    Report("Audio no importado o vacío: " + name, ref errors);
            }
            foreach (string scenePath in new[] { path, "Assets/Scenes/Juego.unity" })
                if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == scenePath))
                    Report("Falta la escena activa de build: " + scenePath, ref errors);
            if (errors == 0) Debug.Log("Correcto: 3 botones originales, ventana de instrucciones, " + MenuRVAudio.ClipNames.Length + " audios importados y las dos escenas de build.");
            else Debug.LogError("La comprobación de Serene Lake RV encontró " + errors + " problema(s).");
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
    }

    static void ValidateXR(Scene scene, Canvas canvas, ref int errors)
    {
        var roots = scene.GetRootGameObjects();
        var origins = roots.SelectMany(root => root.GetComponentsInChildren<XROrigin>(false)).ToArray();
        if (origins.Length != 1) Report("Debe haber un único XR Origin activo en Menu.", ref errors);
        else if (!origins[0].Camera) Report("Falta la cámara del XR Origin.", ref errors);
        else
        {
            var camera = origins[0].Camera;
            float distance = Vector3.Distance(camera.transform.position, canvas.transform.position);
            if (distance < 1f || distance > 2f)
                Report("La cámara inicial debe estar a 1-2 m del menú; distancia: " + distance.ToString("F2") + " m.", ref errors);
            int uiMask = (1 << canvas.gameObject.layer) | (1 << 5);
            if ((camera.cullingMask & uiMask) != uiMask)
                Report("La cámara no muestra todas las capas utilizadas por el menú y sus controles.", ref errors);
        }
        var systems = roots.SelectMany(root => root.GetComponentsInChildren<EventSystem>(false))
            .Where(item => item.enabled).ToArray();
        if (systems.Length != 1) Report("Debe haber un único EventSystem activo en Menu.", ref errors);
        else
        {
            var module = systems[0].GetComponent<XRUIInputModule>();
            if (!module || !module.enabled) Report("Falta el XRUIInputModule activo para el rayo y gatillo.", ref errors);
        }
        var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (!raycaster || !raycaster.enabled) Report("Falta el raycaster de UI XR en el Canvas del menú.", ref errors);
    }

    static void Report(string message, ref int count)
    {
        count++;
        Debug.LogError(message);
    }

    [MenuItem("Tools/Serene Lake RV/Probar todos los sonidos (Play)")]
    public static void PreviewAudio()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Abre Menu, pulsa Play y vuelve a elegir Probar todos los sonidos.");
            return;
        }
        MenuRVAudio.PreviewAllSounds();
    }
}
