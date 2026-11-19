using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// G-38: Ana menü. "Oyna" lobiye geçer, "Çıkış" oyunu kapatır.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;
    [Tooltip("KULLANICI (cila): ayarlar paneli (PauseMenu.OpenSettingsOnly).")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private string lobbyScene = "Lobby";

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (playButton != null) playButton.onClick.AddListener(() => ScreenFader.LoadScene(lobbyScene));
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
        if (settingsButton != null) settingsButton.onClick.AddListener(PauseMenu.OpenSettingsOnly);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
