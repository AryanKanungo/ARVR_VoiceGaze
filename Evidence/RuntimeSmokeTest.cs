using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Collections;
internal class CommandScript : IRunCommand {
SpatialVoiceIntentController c;GazeTargetResolver r;Keyboard k; InputSettings.EditorInputBehaviorInPlayMode previous;InputSettings.BackgroundBehavior previousBackground;bool previousRun;
void Check(bool ok,string label){if(ok)Debug.Log("IVRAR RUNTIME PASS: "+label);else Debug.LogError("IVRAR RUNTIME FAIL: "+label);}
IEnumerator Press(Key key){InputSystem.QueueStateEvent(k,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(k,new KeyboardState());yield return null;}
IEnumerator Test() {
var cam=r.gazeCamera;var red=GameObject.Find("Red Cube").GetComponent<InteractableObject>();var pad=GameObject.Find("Left Pad").GetComponent<InteractableObject>();var original=red.transform.position;var cp=cam.transform.position;var cr=cam.transform.rotation;
yield return Press(Key.Digit2);Check(c.LastMessage.Contains("rejected"),"invalid Place");
cam.transform.LookAt(red.transform.position);yield return new WaitForSecondsRealtime(.3f);Check(r.CurrentStableTarget==red && red.IsGazed,"gaze stabilization");
yield return Press(Key.Digit1);Check(c.SelectedObject==red && red.IsSelected,"keyboard 1 Select");
cam.transform.LookAt(pad.transform.position);yield return new WaitForSecondsRealtime(.3f);Check(r.CurrentStableTarget==pad && red.IsSelected,"pad gaze with persistent selection");
yield return Press(Key.Digit2);Check(Vector3.Distance(red.transform.position,pad.placementAnchor.position)<.001f && c.SelectedObject==null,"keyboard 2 Place");
yield return Press(Key.Digit4);Check(Vector3.Distance(red.transform.position,original)<.001f && !pad.IsOccupied,"keyboard 4 Reset");
cam.transform.LookAt(red.transform.position);yield return new WaitForSecondsRealtime(.3f);
yield return Press(Key.Digit1);Check(c.SelectedObject==red,"reselect");
yield return Press(Key.Digit3);Check(c.SelectedObject==null && !red.IsSelected,"keyboard 3 Release");
cam.transform.LookAt(Vector3.up*10);yield return new WaitForSecondsRealtime(.3f);yield return Press(Key.Digit1);Check(c.LastMessage.Contains("rejected"),"invalid Select");
InputSystem.QueueStateEvent(k,new KeyboardState(Key.W));yield return new WaitForSecondsRealtime(.15f);InputSystem.QueueStateEvent(k,new KeyboardState());yield return null;Check(Vector3.Distance(cp,cam.transform.position)>.01f,"W movement");
cam.transform.SetPositionAndRotation(cp,cr);c.ResetObjects();InputSystem.RemoveDevice(k);InputSystem.settings.editorInputBehaviorInPlayMode=previous;InputSystem.settings.backgroundBehavior=previousBackground;Application.runInBackground=previousRun;Debug.Log("IVRAR RUNTIME TEST COMPLETE");
}
public void Execute(ExecutionResult result) {if(!EditorApplication.isPlaying)throw new System.Exception("Play required");c=Object.FindAnyObjectByType<SpatialVoiceIntentController>();r=c.resolver;previousRun=Application.runInBackground;Application.runInBackground=true;previousBackground=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;previous=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;k=InputSystem.AddDevice<Keyboard>("IVRAR Runtime Test");c.StartCoroutine(Test());result.Log("Started frame-based runtime keyboard tests.");}
}
