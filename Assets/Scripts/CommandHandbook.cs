using UnityEngine;
using UnityEngine.UI;

// Presentation only: command routing, rejection and telemetry stay in the controller.
[DisallowMultipleComponent]
public sealed class CommandHandbook : MonoBehaviour
{
    public GameObject panel;
    public Text body;
    [TextArea] public string platformFooter;
    public bool IsOpen { get; private set; }
    public int Page { get; private set; }
    public int PageCount => Pages.Length;
    static readonly string[] Pages = {
        "WORKBENCH / SELECT & PLACE\n\nSelect  /  Lock the object under your gaze.\nPlace  /  Set the object on a clear destination.\nRelease  /  Clear selection without moving it.\nReset  /  Restore original transforms and colors; remove copies.\n\nSelect starts a task; successful Place completes it.\nRelease or Reset before Place ends that attempt unsuccessfully.\n\nKEYBOARD  1 Select   2 Place   3 Release   4 Reset",
        "FORM / ROTATION\n\nRotate left  /  -45 degrees around local Y.\nRotate right  /  +45 degrees around local Y.\nRotate 45 degrees  /  +45 degrees around local Y.\nRotate 90 degrees  /  +90 degrees around local Y.\nFlip  /  180 degrees around local X.\n\nSelect an object first. Rotations are relative to its current pose.\nUndo restores the most recent supported object edit.",
        "POSITION / MOVEMENT\n\nMove up  /  +0.25 metres in world Y.\nMove down  /  -0.25 metres in world Y.\nMove left  /  -0.25 metres in world X.\nMove right  /  +0.25 metres in world X.\nMove forward  /  +0.25 metres in world Z.\nMove backward  /  -0.25 metres in world Z.\n\nCAMERA  Right mouse + mouse: look\nWASD: move   Q / E: down / up",
        "MATERIAL / SCALE\n\nChange color  /  Cycle chalk, olive, terracotta, blue and ochre.\nMake bigger  /  Increase scale by 25%.\nMake smaller  /  Decrease scale by 25%.\n\nSelect an object first. Scale stays within 50–200% of original.\nReset restores original scale and original material color.\n\nMATERIALS LIBRARY\nGaze at the labeled material control and say Activate\nto cycle the selected object's color.",
        "COPIES / UNDO\n\nDuplicate  /  Create one selectable copy beside the selection.\nDelete  /  Remove only the selected user-created copy.\nUndo  /  Reverse one supported object edit.\n\nAt most five live copies are allowed. Gaze and Select a copy\nto edit or delete it. Original studio objects are protected.\n\nUndo supports movement, rotation, color, scale, placement,\nduplication and deletion. It stores one edit, without redo.\nSelection, lighting, camera and Reset are not undoable.\nReset removes all copies and clears Undo.",
        "STUDIO / CONTROLS\n\nActivate  /  Operate the labeled control under stable gaze.\nTurn lights on  /  Restore normal studio lighting.\nTurn lights off  /  Use a readable daylight safety mode.\nGo home  /  Return the camera to its starting pose.\nWhat am I looking at  /  Show the current stable target name.\n\nLIGHTING CORNER\nGaze at the wall control and say Activate to toggle lighting.\nThe materials control acts on your current selected object.\nThe precision wall remains a display, not a validated experiment.",
        "GUIDE / NAVIGATION\n\nHelp  /  Open this handbook.\nOpen handbook  /  Open this handbook.\nClose handbook  /  Return to interaction.\nNext page  /  Advance one page.\nPrevious page  /  Go back one page.\n\nH: open / close   Arrow keys: previous / next page\n\nObject and studio edits are blocked while the guide is open.\nHelp, navigation and What am I looking at remain available.\nAll command outcomes use the existing CSV telemetry.\nTiming is callback-to-action, not acoustic speech latency."
    };
    void Awake() { IsOpen = false; Page = 0; if (panel) panel.SetActive(false); }
    public bool Navigate(string command, out string message)
    {
        if (!panel || !body) { message = "Handbook is not configured."; return false; }
        switch (command)
        {
            case "open handbook": IsOpen = true; break;
            case "close handbook": IsOpen = false; break;
            case "next page":
                if (!IsOpen) { message = "Open the handbook first."; return false; }
                Page = (Page + 1) % Pages.Length; break;
            case "previous page":
                if (!IsOpen) { message = "Open the handbook first."; return false; }
                Page = (Page + Pages.Length - 1) % Pages.Length; break;
            default: message = "Unknown handbook command."; return false;
        }
        panel.SetActive(IsOpen);
        if (IsOpen) body.text = "VOICEVERSE / THE DESIGN ATELIER\nCOMMAND HANDBOOK                                       " + (Page + 1) + " / " + Pages.Length + "\n________________________________________________\n\n" + Pages[Page] + "\n\n" + (string.IsNullOrEmpty(platformFooter) ? "H Close   |   Next page / Previous page" : platformFooter);
        message = IsOpen ? "Handbook: page " + (Page + 1) + " / " + Pages.Length + ". Object commands paused." : "Handbook closed. Object commands ready.";
        return true;
    }
}
