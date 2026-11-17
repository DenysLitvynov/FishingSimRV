using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Converts materials brought over from the HDRP project (Sprint 1) to URP,
// keeping their textures, colours, normal maps and surface settings.
public static class HdrpToUrpMaterials
{
    // Shader GUIDs of the HDRP package that the original materials point to.
    const string HdrpLit = "6e4ae4064600d784cac1e41a9e6f2e59";
    const string HdrpUnlit = "c4edd00ff2db5b24391a4fcb1762e459";
    const string HdrpTerrainLit = "eb607f5e8fc16e140b732f33d26c8c31";

    [MenuItem("Tools/Conversion/HDRP Materials to URP")]
    public static void ConvertAll()
    {
        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string text = File.ReadAllText(path);
            string kind = text.Contains(HdrpLit) ? "lit" : text.Contains(HdrpUnlit) ? "unlit" : text.Contains(HdrpTerrainLit) ? "terrain" : null;
            if (kind == null) continue;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) continue;
            Convert(material, kind);
            EditorUtility.SetDirty(material);
            converted++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("HDRP to URP: " + converted + " materials converted.");
    }

    struct Saved
    {
        public Dictionary<string, (Texture tex, Vector2 scale, Vector2 offset)> textures;
        public Dictionary<string, float> floats;
        public Dictionary<string, Color> colors;
    }

    // Reads the serialized values directly, because the HDRP shader no longer exists here.
    static Saved Read(Material material)
    {
        var saved = new Saved
        {
            textures = new Dictionary<string, (Texture, Vector2, Vector2)>(),
            floats = new Dictionary<string, float>(),
            colors = new Dictionary<string, Color>()
        };
        var so = new SerializedObject(material);
        var props = so.FindProperty("m_SavedProperties");
        var texEnvs = props.FindPropertyRelative("m_TexEnvs");
        for (int i = 0; i < texEnvs.arraySize; i++)
        {
            var entry = texEnvs.GetArrayElementAtIndex(i);
            var value = entry.FindPropertyRelative("second");
            saved.textures[entry.FindPropertyRelative("first").stringValue] = (
                value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                value.FindPropertyRelative("m_Scale").vector2Value,
                value.FindPropertyRelative("m_Offset").vector2Value);
        }
        var floats = props.FindPropertyRelative("m_Floats");
        for (int i = 0; i < floats.arraySize; i++)
        {
            var entry = floats.GetArrayElementAtIndex(i);
            saved.floats[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").floatValue;
        }
        var colors = props.FindPropertyRelative("m_Colors");
        for (int i = 0; i < colors.arraySize; i++)
        {
            var entry = colors.GetArrayElementAtIndex(i);
            saved.colors[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").colorValue;
        }
        return saved;
    }

    static float Float(Saved s, string name, float fallback) => s.floats.TryGetValue(name, out float v) ? v : fallback;
    static Color Colour(Saved s, string name, Color fallback) => s.colors.TryGetValue(name, out Color c) ? c : fallback;

    static bool CopyTexture(Saved s, string from, Material m, string to)
    {
        if (!s.textures.TryGetValue(from, out var t) || !t.tex) return false;
        m.SetTexture(to, t.tex);
        m.SetTextureScale(to, t.scale);
        m.SetTextureOffset(to, t.offset);
        return true;
    }

    static void Convert(Material m, string kind)
    {
        var s = Read(m);
        if (kind == "terrain")
        {
            m.shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            return;
        }

        bool transparent = Float(s, "_SurfaceType", 0f) > 0.5f;
        bool doubleSided = Float(s, "_DoubleSidedEnable", 0f) > 0.5f;
        bool alphaClip = Float(s, "_AlphaCutoffEnable", 0f) > 0.5f;

        if (kind == "unlit")
        {
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", Colour(s, "_UnlitColor", Color.white));
            CopyTexture(s, "_UnlitColorMap", m, "_BaseMap");
        }
        else
        {
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Colour(s, "_BaseColor", Color.white));
            CopyTexture(s, "_BaseColorMap", m, "_BaseMap");
            m.SetFloat("_Metallic", Float(s, "_Metallic", 0f));
            m.SetFloat("_Smoothness", Float(s, "_Smoothness", 0.5f));
            if (CopyTexture(s, "_NormalMap", m, "_BumpMap"))
                m.SetFloat("_BumpScale", Float(s, "_NormalScale", 1f));
            // HDRP mask map: R metallic, G occlusion, A smoothness — URP reads the same channels.
            if (CopyTexture(s, "_MaskMap", m, "_MetallicGlossMap"))
                CopyTexture(s, "_MaskMap", m, "_OcclusionMap");
            Color emissive = Colour(s, "_EmissiveColor", Color.black);
            if (emissive.maxColorComponent > 0f || s.textures.ContainsKey("_EmissiveColorMap") && s.textures["_EmissiveColorMap"].tex)
            {
                // HDRP stores emission in physical units; bring it back to a 0-1 range.
                float max = Mathf.Max(1f, emissive.maxColorComponent);
                m.SetColor("_EmissionColor", emissive / max);
                CopyTexture(s, "_EmissiveColorMap", m, "_EmissionMap");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
        }

        m.SetFloat("_Surface", transparent ? 1f : 0f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        m.SetFloat("_Cutoff", Float(s, "_AlphaCutoff", 0.5f));
        m.SetFloat("_Cull", doubleSided ? 0f : 2f);
        m.renderQueue = -1;
        UnityEditor.BaseShaderGUI.SetupMaterialBlendMode(m);
        UnityEditor.BaseShaderGUI.SetMaterialKeywords(m);
        if (kind == "lit")
        {
            SetKeyword(m, "_NORMALMAP", m.GetTexture("_BumpMap"));
            SetKeyword(m, "_METALLICSPECGLOSSMAP", m.GetTexture("_MetallicGlossMap"));
            SetKeyword(m, "_OCCLUSIONMAP", m.GetTexture("_OcclusionMap"));
            SetKeyword(m, "_EMISSION", m.GetColor("_EmissionColor").maxColorComponent > 0f);
        }
    }

    static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
    }
}
