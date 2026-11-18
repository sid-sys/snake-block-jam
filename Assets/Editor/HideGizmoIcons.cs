using UnityEditor;
using UnityEngine;

// Hides the white speaker / camera gizmo icons that show up on top of the game board in the Game view.
[InitializeOnLoad]
static class HideGizmoIcons
{
    static HideGizmoIcons() { EditorApplication.delayCall += Apply; }

    static void Apply()
    {
        GizmoUtility.SetIconEnabled(typeof(AudioSource), false);
        GizmoUtility.SetIconEnabled(typeof(AudioListener), false);
        GizmoUtility.SetIconEnabled(typeof(Camera), false);
    }
}
