using System;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    /// <summary>
    /// Plays the game's own button sounds (hover and click). The sounds are read from the
    /// Stats button and played through the same audio route the game uses for its buttons,
    /// so they respect your volume settings.
    /// </summary>
    internal static class UiSounds
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static AudioClip hoverClip;
        private static AudioClip clickClip;
        private static float volume = 1f;
        private static float pitch = 1f;

        // The game's static "PlayHandler" (on its button audio script) that routes UI sounds.
        private static FieldInfo handlerField;
        private static PropertyInfo handlerProperty;

        private static AudioSource fallbackSource;
        private static bool warnedOnce;

        /// <summary>Reads the sounds from one of the game's menu buttons.</summary>
        public static void CaptureFrom(GameObject button)
        {
            foreach (MonoBehaviour script in button.GetComponents<MonoBehaviour>())
            {
                if (script == null)
                    continue;

                Type type = script.GetType();
                FieldInfo hoverField = FindField(type, "hoverEnterClip");
                if (hoverField == null)
                    continue;

                hoverClip = hoverField.GetValue(script) as AudioClip;
                clickClip = FindField(type, "clickClip")?.GetValue(script) as AudioClip;
                volume = ReadFloat(script, type, "volume", 1f);
                pitch = ReadFloat(script, type, "pitch", 1f);

                for (Type t = type; t != null && handlerField == null && handlerProperty == null; t = t.BaseType)
                {
                    handlerField = t.GetField("PlayHandler", StaticFlags | BindingFlags.DeclaredOnly);
                    if (handlerField == null)
                        handlerProperty = t.GetProperty("PlayHandler", StaticFlags | BindingFlags.DeclaredOnly);
                }

                SavesPlugin.Log.LogDebug(
                    $"UI sounds captured (hover: {(hoverClip != null ? hoverClip.name : "none")}, " +
                    $"click: {(clickClip != null ? clickClip.name : "none")}, " +
                    $"game audio route: {(handlerField != null || handlerProperty != null ? "yes" : "no")}).");
                return;
            }

            SavesPlugin.Log.LogWarning("Could not find the game's button sounds; the save list will be silent.");
        }

        public static void PlayHover() => Play(hoverClip);

        public static void PlayClick() => Play(clickClip);

        private static void Play(AudioClip clip)
        {
            if (clip == null)
                return;

            try
            {
                Delegate handler = (handlerField?.GetValue(null) ?? handlerProperty?.GetValue(null)) as Delegate;
                if (handler != null)
                {
                    Type sfxType = handler.GetType().GetMethod("Invoke").GetParameters()[0].ParameterType;
                    handler.DynamicInvoke(CreateSfx(sfxType, clip));
                    return;
                }
            }
            catch (Exception e)
            {
                if (!warnedOnce)
                {
                    warnedOnce = true;
                    SavesPlugin.Log.LogWarning($"Game audio route failed, using a simple fallback: {e.InnerException?.Message ?? e.Message}");
                }
            }

            PlayFallback(clip);
        }

        /// <summary>Builds the game's sound description object (clip + volume + pitch).</summary>
        private static object CreateSfx(Type sfxType, AudioClip clip)
        {
            // Preferred: a constructor like (AudioClip clip, float volume, float pitch).
            foreach (ConstructorInfo ctor in sfxType.GetConstructors(InstanceFlags))
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(AudioClip))
                    continue;

                bool allFloats = true;
                for (int i = 1; i < parameters.Length; i++)
                    if (parameters[i].ParameterType != typeof(float))
                        allFloats = false;
                if (!allFloats)
                    continue;

                var args = new object[parameters.Length];
                args[0] = clip;
                for (int i = 1; i < parameters.Length; i++)
                    args[i] = i == 1 ? volume : i == 2 ? pitch : 1f;
                return ctor.Invoke(args);
            }

            // Otherwise: create it empty and fill in the members by name.
            object sfx;
            try
            {
                sfx = Activator.CreateInstance(sfxType);
            }
            catch
            {
#pragma warning disable SYSLIB0050
                sfx = FormatterServices.GetUninitializedObject(sfxType);
#pragma warning restore SYSLIB0050
            }

            SetMember(sfx, "Clip", clip);
            SetMember(sfx, "Volume", volume);
            SetMember(sfx, "Pitch", pitch);
            return sfx;
        }

        private static void SetMember(object target, string name, object value)
        {
            Type type = target.GetType();
            foreach (FieldInfo field in type.GetFields(InstanceFlags))
            {
                if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    field.Name == $"<{name}>k__BackingField")
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(InstanceFlags))
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.CanWrite)
                {
                    property.SetValue(target, value);
                    return;
                }
            }
        }

        private static void PlayFallback(AudioClip clip)
        {
            if (fallbackSource == null)
            {
                var go = new GameObject("Bestiary UI Audio (mod)");
                Object.DontDestroyOnLoad(go);
                fallbackSource = go.AddComponent<AudioSource>();
                fallbackSource.spatialBlend = 0f;
                fallbackSource.playOnAwake = false;
            }

            fallbackSource.pitch = pitch;
            fallbackSource.PlayOneShot(clip, volume);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static float ReadFloat(object target, Type type, string name, float fallback)
        {
            FieldInfo field = FindField(type, name);
            return field != null && field.GetValue(target) is float value ? value : fallback;
        }
    }
}
