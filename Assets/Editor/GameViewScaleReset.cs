using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity's Game view Scale slider zooms the *window*, not the game camera.
/// Scroll-wheel camera zoom also changes that slider, which crops the shot
/// into a corner. Reset to 1x whenever Play starts.
/// </summary>
[InitializeOnLoad]
static class GameViewScaleReset
{
    static GameViewScaleReset()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            ResetScale();
    }

    [MenuItem("Wilo/Reset Game View Scale")]
    public static void ResetScale()
    {
        Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null)
            return;

        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(gameViewType);
        foreach (UnityEngine.Object obj in windows)
        {
            var window = obj as EditorWindow;
            if (window == null)
                continue;

            MethodInfo snapZoom = gameViewType.GetMethod(
                "SnapZoom",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (snapZoom != null && snapZoom.GetParameters().Length == 1)
            {
                snapZoom.Invoke(window, new object[] { 1f });
                window.Repaint();
                continue;
            }

            FieldInfo zoomAreaField = gameViewType.GetField(
                "m_ZoomArea",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object zoomArea = zoomAreaField?.GetValue(window);
            if (zoomArea == null)
                continue;

            Type zoomType = zoomArea.GetType();
            FieldInfo scaleField = zoomType.GetField("m_Scale", BindingFlags.Instance | BindingFlags.NonPublic);
            scaleField?.SetValue(zoomArea, Vector2.one);

            FieldInfo translationField = zoomType.GetField("m_Translation", BindingFlags.Instance | BindingFlags.NonPublic);
            translationField?.SetValue(zoomArea, Vector2.zero);

            window.Repaint();
        }
    }
}
