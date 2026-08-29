using System;
using System.IO;
using UnityEngine;
using UnityEngine.VFX;

namespace PCG.VFX
{
    /// <summary>
    /// Editor-only bridge: reads the currently tuned Inspector values and writes them to the chosen theme in ThemeDefinitions.json.
    /// Attach it to VFX_MagicCircle, assign the current Visual Effect and renderers, then use the Inspector button.
    /// </summary>
    public class ThemeVfxJsonPresetExporter : MonoBehaviour
    {
        [Header("Source currently being tuned")]
        [SerializeField] private VisualEffect themeVisualEffect;
        [SerializeField] private Renderer baseCircleRenderer;
        [SerializeField] private Renderer slotRenderer;

        [Header("Theme JSON destination")]
        [SerializeField] private string themeId = "Ice";

        public string ThemeId
        {
            get { return themeId; }
        }

#if UNITY_EDITOR
        public bool SaveCurrentInspectorValuesToThemeJson(out string message)
        {
            string jsonPath = Path.Combine(Application.streamingAssetsPath, "Data", "Themes", "ThemeDefinitions.json");
            if (!File.Exists(jsonPath))
            {
                message = "ThemeDefinitions.json was not found: " + jsonPath;
                return false;
            }

            ThemeDefinitionSet definitions = JsonUtility.FromJson<ThemeDefinitionSet>(File.ReadAllText(jsonPath));
            ThemeDefinition theme = FindTheme(definitions, themeId);
            if (theme == null)
            {
                message = "Theme '" + themeId + "' was not found in ThemeDefinitions.json.";
                return false;
            }

            ThemeVfxBindings bindings = definitions.vfxBindings ?? new ThemeVfxBindings();
            if (theme.particleVfx == null)
            {
                theme.particleVfx = new ParticleVfxParameters();
            }

            int savedFieldCount = 0;
            if (themeVisualEffect != null)
            {
                savedFieldCount += TryCopyVfxColor(themeVisualEffect, ResolveBinding(bindings.hdrColorProperty, "SparkColor"), ref theme.particleVfx.sparkColorRgba) ? 1 : 0;
                savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.sizeProperty, "SparkSize"), ref theme.particleVfx.sparkSize) ? 1 : 0;
                savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.lifetimeProperty, "SparkLife"), ref theme.particleVfx.sparkLife) ? 1 : 0;
                savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.yOffsetProperty, "YOffset"), ref theme.particleVfx.yOffset) ? 1 : 0;
                savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.speedProperty, "SparkSpeed"), ref theme.particleVfx.sparkSpeed) ? 1 : 0;
                savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.linearDragProperty, "LinearDrag"), ref theme.particleVfx.linearDrag) ? 1 : 0;

                bool capturedTurbulenceIntensity = TryCopyVfxFloat(
                    themeVisualEffect,
                    ResolveBinding(bindings.turbulenceIntensityProperty, "TurbulenceIntensity"),
                    ref theme.particleVfx.turbulenceIntensity);
                if (capturedTurbulenceIntensity)
                {
                    theme.particleVfx.useTurbulence = theme.particleVfx.turbulenceIntensity > 0.0001f;
                    savedFieldCount++;
                }

                if (theme.particleVfx.useTurbulence)
                {
                    savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.turbulenceDragProperty, "TurbulenceDrag"), ref theme.particleVfx.turbulenceDrag) ? 1 : 0;
                    savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.turbulenceFrequencyProperty, "TurbulenceFrequency"), ref theme.particleVfx.turbulenceFrequency) ? 1 : 0;
                    savedFieldCount += TryCopyVfxOctaves(themeVisualEffect, ResolveBinding(bindings.turbulenceOctavesProperty, "TurbulenceOctaves"), ref theme.particleVfx.turbulenceOctaves) ? 1 : 0;
                    savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.turbulenceRoughnessProperty, "TurbulenceRoughness"), ref theme.particleVfx.turbulenceRoughness) ? 1 : 0;
                    savedFieldCount += TryCopyVfxFloat(themeVisualEffect, ResolveBinding(bindings.turbulenceLacunarityProperty, "TurbulenceLacunarity"), ref theme.particleVfx.turbulenceLacunarity) ? 1 : 0;
                }
            }

            ThemeMaterialBindings materialBindings = definitions.materialBindings ?? new ThemeMaterialBindings();
            Color color;
            if (TryReadRendererColor(baseCircleRenderer, ResolveBinding(materialBindings.baseCircleColorProperty, "_Color"), out color))
            {
                theme.magicCircleColorRgba = ToRgba(color);
                savedFieldCount++;
            }

            if (TryReadRendererColor(slotRenderer, ResolveBinding(materialBindings.slotColorProperty, "_Color"), out color))
            {
                theme.slotColorRgba = ToRgba(color);
                savedFieldCount++;
            }

            if (savedFieldCount == 0)
            {
                message = "No exposed VFX or material values were found. Assign VFX_Theme and the relevant renderers first.";
                return false;
            }

            File.WriteAllText(jsonPath, JsonUtility.ToJson(definitions, true));
            string assetPath = "Assets/StreamingAssets/Data/Themes/ThemeDefinitions.json";
            UnityEditor.AssetDatabase.ImportAsset(assetPath);
            UnityEditor.AssetDatabase.Refresh();
            message = "Saved " + savedFieldCount + " current Inspector values to theme '" + theme.themeId + "'.";
            return true;
        }

        private static ThemeDefinition FindTheme(ThemeDefinitionSet definitions, string requestedThemeId)
        {
            if (definitions == null || definitions.themes == null)
            {
                return null;
            }

            for (int i = 0; i < definitions.themes.Length; i++)
            {
                ThemeDefinition theme = definitions.themes[i];
                if (theme != null && string.Equals(theme.themeId, requestedThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }

            return null;
        }

        private static bool TryCopyVfxColor(VisualEffect vfx, string propertyName, ref float[] destination)
        {
            if (vfx == null || !vfx.HasVector4(propertyName))
            {
                return false;
            }

            destination = ToRgba(vfx.GetVector4(propertyName));
            return true;
        }

        private static bool TryCopyVfxFloat(VisualEffect vfx, string propertyName, ref float destination)
        {
            if (vfx == null || !vfx.HasFloat(propertyName))
            {
                return false;
            }

            destination = vfx.GetFloat(propertyName);
            return true;
        }

        private static bool TryCopyVfxOctaves(VisualEffect vfx, string propertyName, ref int destination)
        {
            if (vfx == null)
            {
                return false;
            }

            if (vfx.HasUInt(propertyName))
            {
                destination = Mathf.Max(1, (int)vfx.GetUInt(propertyName));
                return true;
            }

            if (vfx.HasInt(propertyName))
            {
                destination = Mathf.Max(1, vfx.GetInt(propertyName));
                return true;
            }

            return false;
        }

        private static bool TryReadRendererColor(Renderer renderer, string propertyName, out Color color)
        {
            color = Color.white;
            if (renderer == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(propertyName))
            {
                return false;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            color = block.isEmpty ? renderer.sharedMaterial.GetColor(propertyName) : block.GetColor(propertyName);
            return true;
        }

        private static string ResolveBinding(string dataBinding, string fallbackBinding)
        {
            return string.IsNullOrEmpty(dataBinding) ? fallbackBinding : dataBinding;
        }

        private static float[] ToRgba(Vector4 value)
        {
            return new[] { value.x, value.y, value.z, value.w };
        }

        private static float[] ToRgba(Color value)
        {
            return new[] { value.r, value.g, value.b, value.a };
        }
#endif
    }
}
