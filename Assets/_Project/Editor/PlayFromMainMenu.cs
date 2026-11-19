using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// KULLANICI: Editörde Play'e basınca oyun her zaman ana menüden başlasın (hangi sahne açık olursa olsun).
/// Build'de zaten Build Settings'teki ilk sahne (MainMenu) açılır.
/// Kapatmak için: menü Sisli Ada > Play: Ana Menüden Başlat.
/// </summary>
[InitializeOnLoad]
public static class PlayFromMainMenu
{
    private const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";
    private const string PrefKey = "SisliAda.PlayFromMainMenu";
    private const string MenuPath = "Sisli Ada/Play: Ana Menüden Başlat";

    static PlayFromMainMenu()
    {
        EditorApplication.delayCall += Apply;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    private static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) : null;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
