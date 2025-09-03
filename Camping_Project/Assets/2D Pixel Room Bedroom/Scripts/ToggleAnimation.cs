using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections.Generic;

public class ToggleAnimation : MonoBehaviour {
	public Toggle Toggle;
	private bool ToggleValues;
    public Animation Animation;
	public string on = "";
	public string off = "";

	private void Start() {
		Toggle.onValueChanged.AddListener((bool tempVar) => {
			ToggleValues = tempVar;
			OnToggleClick(Toggle, ToggleValues);
		});
	}

	public void OnToggleClick(Toggle toggle, bool value) {

			if(Toggle.isOn) {
				Animation.Play(on);
			} else {
				Animation.Play(off);
			}
	}
}
