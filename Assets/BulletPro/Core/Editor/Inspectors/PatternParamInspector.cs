﻿using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

// This script is part of the BulletPro package for Unity.
// Author : Simon Albou <albou.simon@gmail.com>

namespace BulletPro.EditorScripts
{
	// A struct used for storing the names of several possible instructions
	public class StringToEnumInstruction : UnityEngine.Object
	{
		public string menuName, displayName;
		public PatternInstructionType enumValue;

		public StringToEnumInstruction(string _name, string _displayName, PatternInstructionType _enumValue)
		{
			menuName = _name;
			enumValue = _enumValue;
			displayName = _displayName;
		}
	}

	[CustomEditor(typeof(PatternParams))]
	public class PatternParamInspector : EmissionParamsInspector
	{
		// Instruction stack
		ReorderableList tagList;
		ReorderableList instRList;
		ReorderableList delaylessList;
		SerializedProperty playAtBulletBirth, compensateSmallWaits, deltaTimeDisplacement, patternTags, instructions, safetyForPlaymode;
		SerializedProperty defaultInstructionDelay, delaylessInstructions, advancedFoldout;
		SerializedProperty focusedInstruction;

		// Styles and resources
		string disabledModuleStyle,	enabledModuleStyle;
		GUIStyle bigDropdownStyle, normalButtonStyle, focusedButtonStyle;
		Texture2D[] gradientBGs;
		Texture2D clockIcon;
		StringToEnumInstruction[] possibleInstructions, possibleCurveInstructions;
		GenericMenu genericMenu;

		// list / generic menu / color helpers
		int indexOfOpenedMenu;
		SerializedProperty instPropOfOpenedMenu;
		int currentLoopLevel;
		bool previousWasEndLoop;

		// PostOnEnable is a custom callback done after OnEnable (during OnInspectorGUI) to ensure EditorStyles are loaded
		bool hasCalledPostOnEnable;

		// targets
		PatternParams pp;

		public override void OnEnable()
		{
			if (target == null)
			{
				OnUnselected();
				DestroyImmediate(this);
				return;
			}
			
			base.OnEnable();

			playAtBulletBirth = serializedObject.FindProperty("playAtBulletBirth");
			compensateSmallWaits = serializedObject.FindProperty("compensateSmallWaits");
			deltaTimeDisplacement = serializedObject.FindProperty("deltaTimeDisplacement");
			patternTags = serializedObject.FindProperty("patternTags");
			SerializedProperty parallelInstArray = serializedObject.FindProperty("instructionLists");
			if (parallelInstArray.arraySize != 1)
			{
				parallelInstArray.arraySize = 1;
				serializedObject.ApplyModifiedPropertiesWithoutUndo();
			}
			instructions = parallelInstArray.GetArrayElementAtIndex(0).FindPropertyRelative("instructions");
			safetyForPlaymode = serializedObject.FindProperty("safetyForPlaymode");
			focusedInstruction = serializedObject.FindProperty("focusedInstruction");
			
			advancedFoldout = serializedObject.FindProperty("advancedFoldout");
			delaylessInstructions = serializedObject.FindProperty("delaylessInstructions");
			defaultInstructionDelay = serializedObject.FindProperty("defaultInstructionDelay");

			disabledModuleStyle = "Label";
			enabledModuleStyle = "Box";

			clockIcon = EditorGUIUtility.FindTexture("UnityEditor.AnimationWindow");

			gradientBGs = new Texture2D[4];
			if (!EditorGUIUtility.isProSkin)
			{
				Color baseBGColor = new Color(0.1f, 0.9f, 1.0f, 0.45f);
				for (int i = 0; i < gradientBGs.Length; i++)
				{
					float len = (float)gradientBGs.Length;
					float gradLight = 1f - ((((float)i)+1.0f)/len);
					Color usedCol = baseBGColor * gradLight;
					usedCol.g *= Mathf.Lerp(gradLight, 1.0f, 0.8f);
					usedCol.a = baseBGColor.a;
					gradientBGs[i] = GradientTex(usedCol);
				}
			}
			else
			{
				Color baseBGColor = new Color(0.1f, 0.9f, 1.0f, 0.7f);
				for (int i = 0; i < gradientBGs.Length; i++)
				{
					float len = (float)gradientBGs.Length;
					float gradAlpha = ((((float)i)+1.0f)/len);
					Color usedCol = baseBGColor;
					usedCol.a = baseBGColor.a * gradAlpha;
					gradientBGs[i] = GradientTex(usedCol);
				}
			}

			SetupDelaylessInstructions();
			SetupPossibleInstructions();
			SetupInstructionList();
			SetupTagList();
			SetupGenericMenu();

			pp = target as PatternParams;
			
			if (!pp.hasBeenSerializedOnce)
				pp.FirstInitialization();
	
			if (!EditorApplication.isPlaying)
				pp.SetUniqueIndex();

			Undo.undoRedoPerformed += OnUndo;
		}

		public void OnUndo()
		{
			Repaint();
		}

		public override void OnDisable()
		{
			OnUnselected();
			Undo.undoRedoPerformed -= OnUndo;
		}

		// GUIStyles are initialized here
		void PostOnEnable()
		{
			hasCalledPostOnEnable = true;
			bigDropdownStyle = new GUIStyle(EditorStyles.popup);
			bigDropdownStyle.active = bigDropdownStyle.normal;

			normalButtonStyle = new GUIStyle("button");
			focusedButtonStyle = new GUIStyle("button");
			focusedButtonStyle.normal = focusedButtonStyle.active;
		}

        public override bool UseDefaultMargins() { return false; }
		
		public override void OnInspectorGUI()
		{
			if (!hasCalledPostOnEnable) PostOnEnable();

			// Debug
			// System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
			// stopwatch.Start();

			base.OnInspectorGUI();

			EditorGUILayout.PropertyField(playAtBulletBirth, new GUIContent("Play at Start",
				"Plays the pattern upon bullet birth. If this is set to false, you'll have to call Play() manually somewhere else."));

			GUILayout.Space(8);
			float oldLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = 80;
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(0);
			EditorGUILayout.BeginVertical();
			tagList.DoLayoutList();
			EditorGUILayout.EndVertical();
			EditorGUILayout.EndHorizontal();
			EditorGUIUtility.labelWidth = oldLabelWidth;

			EditorGUI.BeginChangeCheck();
			
			GUILayout.Space(8);
			currentLoopLevel = 0;
			previousWasEndLoop = false;
			instRList.DoLayoutList();
			if (HasInfiniteLoop())
			{
				GUILayout.Space(8);
				EditorGUILayout.HelpBox("The list above contains an endless loop with no enabled \"Wait\" instruction with Wait time greater than 0.\nIf you don't use the Instruction Delay, loop iteration time will be as short as one frame.", MessageType.Warning);
			}

			if (focusedInstruction.intValue > -1 && focusedInstruction.intValue < instructions.arraySize)
			{
				SerializedProperty focusedInst = instructions.GetArrayElementAtIndex(focusedInstruction.intValue);
				SerializedProperty canBeDoneOverTime = focusedInst.FindPropertyRelative("canBeDoneOverTime");
				if (canBeDoneOverTime.boolValue)
				{
					GUILayout.Space(12);
					EditorGUILayout.BeginVertical("box");
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Timing Settings: "+focusedInst.FindPropertyRelative("displayName").stringValue, EditorStyles.boldLabel);
					Color defC = GUI.color;
					GUI.color = new Color(1.0f, 0.7f, 0.7f, 1.0f);
					if (GUILayout.Button("Close this", EditorStyles.miniButton, GUILayout.MaxWidth(70)))
						focusedInstruction.intValue = -1;
					GUI.color = defC;
					EditorGUILayout.EndHorizontal();
					EditorGUI.indentLevel += 2;

					#region reminding specific instruction params

					SerializedProperty instType = focusedInst.FindPropertyRelative("instructionType");
					PatternInstructionType pit = (PatternInstructionType)instType.enumValueIndex;
					if (IsMultiplyInstruction(pit))
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("factor"));
					else if (IsRotationInstruction(pit))
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("rotation"), new GUIContent("Degrees"));
					else if (IsSetSpeedInstruction(pit))
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("speedValue"), new GUIContent("New Value"));
					else if (PromptsForSingleColor(pit))
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("color"), new GUIContent("Color"));
					else if (pit == PatternInstructionType.TranslateGlobal)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("globalMovement"), new GUIContent("Movement"));
					else if (pit == PatternInstructionType.TranslateLocal)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("localMovement"), new GUIContent("Movement"));
					else if (pit == PatternInstructionType.SetWorldPosition)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("globalMovement"), new GUIContent("New Value"));
					else if (pit == PatternInstructionType.SetLocalPosition)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("localMovement"), new GUIContent("New Value"));
					else if (pit == PatternInstructionType.SetScale)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("scaleValue"), new GUIContent("New Value"));
					else if (pit == PatternInstructionType.TurnToTarget)
					{
						EditorGUILayout.HelpBox("-1 is \"Look Away From Target\".\n0 is \"Do Nothing\".\n1 is \"Look At Target\".", MessageType.None);
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("turnIntensity"));
					}
					else if (pit == PatternInstructionType.SetCurveValue)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("newCurveValue"));
					else if (pit == PatternInstructionType.SetPeriod)
					{
						SerializedProperty periodType = focusedInst.FindPropertyRelative("newPeriodType");
						EditorGUILayout.PropertyField(periodType);
						if (periodType.enumValueIndex == (int)CurvePeriodType.FixedValue)
							EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("newPeriodValue"));
					}
					else if (pit == PatternInstructionType.SetCurveRawTime)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("curveRawTime"));
					else if (pit == PatternInstructionType.SetCurveRatio)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("curveTime"));
					else if (pit == PatternInstructionType.SetAlpha || pit == PatternInstructionType.AddAlpha)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("alpha"));
					else if (pit == PatternInstructionType.SetLifetimeGradient)
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("gradient"));
					else if (PromptsForCustomParam(pit))
					{
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("customParamName"));
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative(GetRelevantPropertyForCustomParam(pit)));						
					}
					#endregion
					
					SerializedProperty instructionTiming = focusedInst.FindPropertyRelative("instructionTiming");
					EditorGUILayout.PropertyField(instructionTiming);
					if (instructionTiming.enumValueIndex == (int)InstructionTiming.Progressively)
					{
						SerializedProperty instDuration = focusedInst.FindPropertyRelative("instructionDuration");
						EditorGUI.BeginChangeCheck();
						EditorGUILayout.PropertyField(instDuration);
						if (EditorGUI.EndChangeCheck())
							DynamicParameterUtility.ClampAboveZero(instDuration);
						EditorGUILayout.PropertyField(focusedInst.FindPropertyRelative("operationCurve"));
					}
					EditorGUI.indentLevel -= 2;
					EditorGUILayout.EndVertical();
				}
			}

			GUILayout.Space(16);
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(8);
			advancedFoldout.boolValue = EditorGUILayout.Foldout(advancedFoldout.boolValue, "Advanced Settings", true);
			EditorGUILayout.EndHorizontal();
			if (advancedFoldout.boolValue)
			{
				EditorGUI.indentLevel += 2;

				EditorGUILayout.PropertyField(compensateSmallWaits, new GUIContent("Compensate Small Waits",
				"If this is enabled and you have multiple Wait instructions that are really small (smaller than one frame), they will be skipped when necessary to simulate a higher framerate.\n"+
				"If this is disabled, every Wait instruction will last at least one frame (16ms).\n"+
				"Enabling this can incur a slight loss of accuracy, that can be recovered by checking \"Delta Time Displacement\" below."));

				EditorGUILayout.PropertyField(deltaTimeDisplacement, new GUIContent("Delta Time Displacement", "If this is enabled, bullets fired by this Pattern will have their spawn point slightly offset in order to simulate different timesteps within a single frame.\n"+
				"This can greatly improve overall pattern accuracy."));

				EditorGUILayout.PropertyField(defaultInstructionDelay, new GUIContent("Instruction Delay", "If this is greater than zero, the pattern will wait this many seconds after every instruction, except those marked as \"delayless\" just below."));
				EditorGUI.indentLevel -= 2;

				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(28);
				EditorGUILayout.BeginVertical();
				delaylessList.DoLayoutList();
				EditorGUILayout.EndVertical();
				EditorGUILayout.EndHorizontal();
			}

			if (EditorGUI.EndChangeCheck())
			{
				safetyForPlaymode.intValue++;
				serializedObject.ApplyModifiedProperties();
			}

			if (EditorApplication.isPlaying)
			{
				GUILayout.Space(16);
				EditorGUILayout.LabelField("Ingame debugging actions :");
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Kill all bullets (except root emitter)", EditorStyles.miniButton))
				{
					#if UNITY_2023_1_OR_NEWER
					BulletEmitter[] be = UnityEngine.Object.FindObjectsByType(typeof(BulletEmitter), FindObjectsSortMode.None) as BulletEmitter[];
					#else
					BulletEmitter[] be = GameObject.FindObjectsOfType(typeof(BulletEmitter)) as BulletEmitter[];
					#endif
					if (be != null)
						if (be.Length > 0)
							for (int i = 0; i < be.Length; i++)
								if (be[i].emitterProfile == pp.profile)
									be[i].Kill(KillOptions.AllBulletsButRoot);
				}

				if (GUILayout.Button("Restart pattern", EditorStyles.miniButton))
				{
					safetyForPlaymode.intValue++;
					serializedObject.ApplyModifiedProperties();
				}
				EditorGUILayout.EndHorizontal();
			
				GUILayout.Space(8);
				EditorGUILayout.HelpBox("You can edit the instruction parameters in Play Mode.\n"+
				"Whenever you do so, the pattern will restart.\n"+
				"At any other time, you can also manually click the buttons above.", MessageType.Info);
			}
			
			ApplyAll();

			// Debug
			//EditorGUILayout.LabelField(safetyForPlaymode.intValue.ToString());
			//stopwatch.Stop();
			//EditorGUILayout.LabelField(stopwatch.ElapsedTicks.ToString());
			//EditorGUILayout.LabelField(pp.uniqueIndex.ToString());
		}

		void ApplyAll()
		{
			EditorUtility.SetDirty(pp);
			serializedObject.ApplyModifiedProperties();
		}

		// Property drawer of a instruction
		void InstructionDrawer(Rect rect, int index, bool isActive, bool isFocused)
		{
			#region global stuff

			int oldIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			float oldLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = 55;
			float oldFieldWidth = EditorGUIUtility.fieldWidth;

			rect.y += 2;
			float h = EditorGUIUtility.singleLineHeight;
			float w1 = 25; // enabled tickbox rect
			float w2 = 130; // enum rect
			float w3 = 25; // space between enum rect and what follows

			Rect enableRect = new Rect(rect.x, rect.y, w1, h);
			Rect enumRect = new Rect(rect.x + w1, rect.y, w2, h);
			Rect usableSpace = new Rect(rect.x + w1 + w2 + w3, rect.y, rect.width - (w1 + w2 + w3), h);

			SerializedProperty inst = instructions.GetArrayElementAtIndex(index);
			SerializedProperty instType = inst.FindPropertyRelative("instructionType");
			SerializedProperty displayName = inst.FindPropertyRelative("displayName");
			SerializedProperty isEnabled = inst.FindPropertyRelative("enabled");
			SerializedProperty canBeDoneOverTime = inst.FindPropertyRelative("canBeDoneOverTime");
			PatternInstructionType pit = (PatternInstructionType)instType.enumValueIndex;

			// color background based on loops
			Rect backgroundRect = new Rect(rect.x-18, rect.y-2, rect.width+5+18, rect.height);
			if (previousWasEndLoop)
			{
				currentLoopLevel--;
				previousWasEndLoop = false;
			}
			if (isEnabled.boolValue)
			{
				if (pit == PatternInstructionType.BeginLoop)
					currentLoopLevel++;
				else if (currentLoopLevel > 0 && instType.enumValueIndex == (int)PatternInstructionType.EndLoop)
					previousWasEndLoop = true;
			}
			if (currentLoopLevel > 0)
				GUI.DrawTexture(backgroundRect, gradientBGs[(currentLoopLevel-1) % gradientBGs.Length], ScaleMode.StretchToFill, true);

			// "Enabled" tickbox
			EditorGUI.BeginChangeCheck();
			EditorGUI.PropertyField(enableRect, isEnabled, GUIContent.none);
			if (EditorGUI.EndChangeCheck())
			{
				safetyForPlaymode.intValue++;
				serializedObject.ApplyModifiedProperties();
				if (pit == PatternInstructionType.Shoot)
				{
					PatternParams newParent = isEnabled.boolValue ? pp : null;
					EmitterProfileUtility.SetParentOfShot(pp.instructionLists[0].instructions[index].shot, newParent, this);					
				}
			}

			EditorGUI.BeginDisabledGroup(!isEnabled.boolValue); // ends at the very end of the drawer, hundreds of lines later
			
			// Dropdown
			if (GUI.Button(enumRect, displayName.stringValue, bigDropdownStyle))
			{
				indexOfOpenedMenu = index;
				instPropOfOpenedMenu = instructions.GetArrayElementAtIndex(index);
				genericMenu.DropDown(enumRect);
			}

			// "Turn into micro-action" button
			if (canBeDoneOverTime.boolValue)
			{
				float timeBtnWidth = 19;
				float antiMargin = -w3;
				float spaceAroundBtnRect = 3f;
				Rect timeBtnRect = new Rect(usableSpace.x + antiMargin + spaceAroundBtnRect, usableSpace.y, timeBtnWidth, usableSpace.height-1);
				
				bool isProgressive = inst.FindPropertyRelative("instructionTiming").enumValueIndex == (int)InstructionTiming.Progressively;
				Color defC = GUI.color;
				if (isProgressive) GUI.color = Color.green;
				GUIStyle btnStyleToUse = (focusedInstruction.intValue == index) ? focusedButtonStyle : normalButtonStyle;
				if (GUI.Button(timeBtnRect, new GUIContent("", "This action can be executed over time.\nClick for settings."), btnStyleToUse))
				{
					if (focusedInstruction.intValue == index) focusedInstruction.intValue = -1;
					else focusedInstruction.intValue = index;
				}
				GUI.color = defC;

				timeBtnRect.x += 2; timeBtnRect.y += 1;
				timeBtnRect.width -= 3; timeBtnRect.height -= 1;
				GUI.DrawTexture(timeBtnRect, clockIcon);
			}

			#endregion

			#region prompting for instruction params

			if (PromptsForSingleProperty(pit))
			{
				SerializedProperty uniqueProp = null;
				if (IsMultiplyInstruction(pit)) uniqueProp = inst.FindPropertyRelative("factor");
				else if (IsSetSpeedInstruction(pit)) uniqueProp = inst.FindPropertyRelative("speedValue");
				else if (IsRotationInstruction(pit)) uniqueProp = inst.FindPropertyRelative("rotation");
				else if (PromptsForSingleColor(pit)) uniqueProp = inst.FindPropertyRelative("color");
				
				else if (pit == PatternInstructionType.Wait) uniqueProp = inst.FindPropertyRelative("waitTime");
				else if (pit == PatternInstructionType.PlayAudio) uniqueProp = inst.FindPropertyRelative("audioClip");
				
				else if (pit == PatternInstructionType.TranslateGlobal) uniqueProp = inst.FindPropertyRelative("globalMovement");
				else if (pit == PatternInstructionType.SetWorldPosition) uniqueProp = inst.FindPropertyRelative("globalMovement");
				else if (pit == PatternInstructionType.TranslateLocal) uniqueProp = inst.FindPropertyRelative("localMovement");
				else if (pit == PatternInstructionType.SetLocalPosition) uniqueProp = inst.FindPropertyRelative("localMovement");

				else if (pit == PatternInstructionType.SetScale) uniqueProp = inst.FindPropertyRelative("scaleValue");
				else if (pit == PatternInstructionType.TurnToTarget) uniqueProp = inst.FindPropertyRelative("turnIntensity");
				else if (pit == PatternInstructionType.ChangeTarget) uniqueProp = inst.FindPropertyRelative("preferredTarget");

				else if (pit == PatternInstructionType.SetWrapMode) uniqueProp = inst.FindPropertyRelative("newWrapMode");
				else if (pit == PatternInstructionType.SetCurveRawTime) uniqueProp = inst.FindPropertyRelative("curveRawTime");
				else if (pit == PatternInstructionType.SetCurveRatio) uniqueProp = inst.FindPropertyRelative("curveTime");

				else if (pit == PatternInstructionType.SetAlpha) uniqueProp = inst.FindPropertyRelative("alpha");
				else if (pit == PatternInstructionType.AddAlpha) uniqueProp = inst.FindPropertyRelative("alpha");
				else if (pit == PatternInstructionType.SetLifetimeGradient) uniqueProp = inst.FindPropertyRelative("gradient");
				
				else if (pit == PatternInstructionType.PlayPattern) uniqueProp = inst.FindPropertyRelative("patternTag");
				else if (pit == PatternInstructionType.SetInstructionDelay) uniqueProp = inst.FindPropertyRelative("waitTime");
				else if (pit == PatternInstructionType.SetRandomSeed) uniqueProp = inst.FindPropertyRelative("newRandomSeed");

				EditorGUI.PropertyField(usableSpace, uniqueProp, GUIContent.none);

				// Clamp some values
				if (pit == PatternInstructionType.Wait || pit == PatternInstructionType.SetInstructionDelay)
					DynamicParameterUtility.ClampAboveZero(uniqueProp);
			}
			else if (PromptsForEnumPlusRect(pit))
			{
				SerializedProperty enumProp = null;
				SerializedProperty secondProp = null;

				float spaceBetween = 10f;
				float halfWidth = (usableSpace.width-spaceBetween)*0.5f;
				Rect enumPropRect = new Rect(usableSpace.x, usableSpace.y, halfWidth, usableSpace.height);
				Rect secondPropRect = new Rect(usableSpace.x + halfWidth + spaceBetween, usableSpace.y, halfWidth, usableSpace.height);

				if (pit == PatternInstructionType.PlayVFX || pit == PatternInstructionType.StopVFX)
				{
					enumProp = inst.FindPropertyRelative("vfxFilterType");

					EditorGUI.PropertyField(enumPropRect, enumProp, GUIContent.none);

					if (enumProp.enumValueIndex == (int)VFXFilterType.Index)
						secondProp = inst.FindPropertyRelative("vfxIndex");
					else if (enumProp.enumValueIndex == (int)VFXFilterType.Tag)
						secondProp = inst.FindPropertyRelative("vfxTag");

					EditorGUI.PropertyField(secondPropRect, secondProp, GUIContent.none);
				}
				else if (pit == PatternInstructionType.BeginLoop)
				{
					enumProp = inst.FindPropertyRelative("endless");

					string[] options = new string[] { "Endless", "Set Count" };
					enumProp.boolValue = EditorGUI.Popup(enumPropRect, enumProp.boolValue?0:1, options) == 0;
					
					if (!enumProp.boolValue)
					{
						secondProp = inst.FindPropertyRelative("iterations");
						EditorGUI.PropertyField(secondPropRect, secondProp, GUIContent.none);
						DynamicParameterUtility.ClampIntAboveZero(secondProp);
					}
				}
				else if (pit == PatternInstructionType.ChangeHomingTag
					|| pit == PatternInstructionType.ChangeCollisionTag)
				{
					enumProp = inst.FindPropertyRelative("collisionTagAction");
					secondProp = inst.FindPropertyRelative("collisionTag");

					EditorGUI.PropertyField(enumPropRect, enumProp, GUIContent.none);
					EditorGUI.PropertyField(secondPropRect, secondProp, GUIContent.none);
				}
				else if (pit == PatternInstructionType.SetPeriod)
				{
					enumProp = inst.FindPropertyRelative("newPeriodType");

					EditorGUI.PropertyField(enumPropRect, enumProp, GUIContent.none);
					if (enumProp.enumValueIndex == (int)CurvePeriodType.FixedValue)
					{
						secondProp = inst.FindPropertyRelative("newPeriodValue");
						EditorGUI.PropertyField(secondPropRect, secondProp, GUIContent.none);
					}
				}
				else if (pit == PatternInstructionType.PausePattern
					|| pit == PatternInstructionType.StopPattern
					|| pit == PatternInstructionType.RebootPattern)
				{
					enumProp = inst.FindPropertyRelative("patternControlTarget");
					string[] options = new string[] { "This Pattern (self)", "Another Pattern (from Tag)" };
					enumProp.enumValueIndex = EditorGUI.Popup(enumPropRect, enumProp.enumValueIndex, options);
					
					//EditorGUI.PropertyField(enumPropRect, enumProp, GUIContent.none);
					if (enumProp.enumValueIndex == (int)PatternControlTarget.AnotherPattern)
					{
						secondProp = inst.FindPropertyRelative("patternTag");
						EditorGUI.PropertyField(secondPropRect, secondProp, GUIContent.none);
					}
				}
			}
			else if (PromptsForCustomParam(pit))
			{
				SerializedProperty nameProp = inst.FindPropertyRelative("customParamName");
				SerializedProperty customProp = inst.FindPropertyRelative(GetRelevantPropertyForCustomParam(pit));

				float spaceBetween = 10f;
				float halfWidth = (usableSpace.width-spaceBetween)*0.5f;
				Rect namePropRect = new Rect(usableSpace.x, usableSpace.y, halfWidth, usableSpace.height);
				Rect customPropRect = new Rect(usableSpace.x + halfWidth + spaceBetween, usableSpace.y, halfWidth, usableSpace.height);	

				if (pit == PatternInstructionType.SetCustomAnimationCurve)
				{
					SerializedProperty forceZeroToOne = customProp.FindPropertyRelative("forceZeroToOne");
					forceZeroToOne.boolValue = false;
				}

				EditorGUI.PropertyField(namePropRect, nameProp, GUIContent.none);
				EditorGUI.PropertyField(customPropRect, customProp, GUIContent.none);
			}
			else if (pit == PatternInstructionType.Shoot)
			{
				EditorGUI.BeginChangeCheck();
				SerializedProperty sp = inst.FindPropertyRelative("shot");
				fieldHandler.DynamicParamField<ShotParams>(usableSpace, GUIContent.none, sp);
				if (EditorGUI.EndChangeCheck())
				{
					safetyForPlaymode.intValue++;
					serializedObject.ApplyModifiedProperties();
				}
			}
			else if (pit == PatternInstructionType.SetCurveValue)
			{
				SerializedProperty cv = inst.FindPropertyRelative("newCurveValue");
				SerializedProperty forceZeroToOne = cv.FindPropertyRelative("forceZeroToOne");
				forceZeroToOne.boolValue = false;
				EditorGUI.PropertyField(usableSpace, cv, GUIContent.none);
				forceZeroToOne.boolValue = true;
			}

			#endregion
			
			#region end of drawer

			EditorGUI.EndDisabledGroup();
			EditorGUI.indentLevel = oldIndent;
			EditorGUIUtility.labelWidth = oldLabelWidth;
			EditorGUIUtility.fieldWidth = oldFieldWidth;

			#endregion
		}

		// Not exactly the same function as in BulletParams, the inspector is a bit different. EDIT : no longer in use.
		void DrawDynamicCurveInspector(SerializedProperty curve, string displayName="")
		{
			// Curve name and enabling
			FontStyle defaultLabelStyle = EditorStyles.label.fontStyle;
			SerializedProperty spIterator = curve.Copy();
			spIterator.NextVisible(true); // enabled
			EditorStyles.label.fontStyle = FontStyle.Bold;
			EditorGUILayout.BeginVertical(spIterator.boolValue ? enabledModuleStyle : disabledModuleStyle);			
			string str = displayName; if (str=="") displayName = curve.displayName;
			GUIContent curveGC = new GUIContent(displayName, displayName);
			spIterator.boolValue = EditorGUILayout.Toggle(curveGC, spIterator.boolValue);
			EditorStyles.label.fontStyle = defaultLabelStyle;
			if (!spIterator.boolValue)
			{
				EditorGUILayout.EndVertical();
				return;
			}

			// Curve properties

			EditorGUI.indentLevel += 1;				

			spIterator.NextVisible(false); // wrapmode

			EditorGUILayout.PropertyField(spIterator);
			spIterator.NextVisible(false); // period is lifespan
			EditorGUILayout.PropertyField(spIterator, new GUIContent("Period is Bullet Lifespan"));
			//if (!DynamicParameterUtility.GetBool(spIterator, false, false))
			if (DynamicParameterUtility.CanBeFalse(spIterator))
			{
				// display warning before other parameters if "period is lifespan" is non-fixed
				if (DynamicParameterUtility.CanBeTrue(spIterator))
				{
					if (!pp.parent)
						EditorGUILayout.HelpBox("This curve's period is set to sometimes match parent bullet's lifespan, but there is no parent bullet.", MessageType.Error);
					else if (!((pp.parent as BulletParams).hasLifespan))
						EditorGUILayout.HelpBox("This curve's period is set to sometimes match parent bullet's lifespan, but this bullet has no limited lifespan.", MessageType.Error);
				}

				spIterator.NextVisible(false); // period
				SerializedProperty spPeriod = spIterator.Copy();
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(spPeriod);
				if (EditorGUI.EndChangeCheck())
					DynamicParameterUtility.ClampAboveZero(spPeriod);
			}
			else
			{
				spIterator.NextVisible(false); // period
				if (!pp.parent)
					EditorGUILayout.HelpBox("This curve's period is set to match parent bullet's lifespan, but there is no parent bullet.", MessageType.Error);
				else if (!((pp.parent as BulletParams).hasLifespan))
					EditorGUILayout.HelpBox("This curve's period is set to match parent bullet's lifespan, but this bullet has no limited lifespan.", MessageType.Error);
			}

			spIterator.NextVisible(false); // curve
			EditorGUILayout.PropertyField(spIterator);
			ApplyAll();

			EditorGUI.indentLevel -= 1;

			EditorGUILayout.EndVertical();
		}

		// Detects infinite loop lacking Wait instruction in instruction stack
		bool HasInfiniteLoop()
		{
			if (instructions.arraySize == 0) return false;

			bool result = false;
			bool isInEndlessLoop = false;
			int nestedLoops = 0;

			for (int i = 0; i < instructions.arraySize; i++)
			{
				SerializedProperty inst = instructions.GetArrayElementAtIndex(i);

				// disregard disabled instructions
				if (inst.FindPropertyRelative("enabled").boolValue == false) continue;

				SerializedProperty instType = inst.FindPropertyRelative("instructionType");

				// are we entering a loop ?
				if (instType.enumValueIndex == (int)PatternInstructionType.BeginLoop)
				{
					nestedLoops++; // endless or not, we're one level deeper
					if (inst.FindPropertyRelative("endless").boolValue)
					{
						nestedLoops = 0; // reset depth counter. If this one has Wait, so does its parent
						isInEndlessLoop = true;
						result = true;
					}
				}

				// are we exiting the endless loop ?
				if (instType.enumValueIndex == (int)PatternInstructionType.EndLoop)
				{
					nestedLoops--;
					if (nestedLoops < 0)
						isInEndlessLoop = false;
				}

				// if we're in an endless loop, does it yield here ?
				if (!isInEndlessLoop) continue;
				if (instType.enumValueIndex == (int)PatternInstructionType.Wait && DynamicParameterUtility.IsAlwaysAboveZero(inst.FindPropertyRelative("waitTime")))
					result = false;
			}

			return result;
		}

		#region OnEnable toolbox

		// Sets up the reorderable list for patternTags
		void SetupTagList()
		{
			tagList = new ReorderableList(serializedObject, patternTags, true, true, true, true);

			tagList.drawHeaderCallback = (Rect rect) =>
			{
				EditorGUI.LabelField(rect, "Pattern Tags (recommended if \"Play at Start\" is off)");		
			};
			tagList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
			{
				rect.y += 2;
				rect.height = EditorGUIUtility.singleLineHeight;
				EditorGUI.PropertyField(rect, patternTags.GetArrayElementAtIndex(index), new GUIContent("Tag "+index.ToString()));
			};
			tagList.onRemoveCallback += (ReorderableList list) =>
			{
				patternTags.DeleteArrayElementAtIndex(list.index);
			};
			tagList.onAddCallback += (ReorderableList list) =>
			{
				patternTags.arraySize++;
				DynamicParameterUtility.SetFixedString(patternTags.GetArrayElementAtIndex(patternTags.arraySize-1), "", true);
				serializedObject.ApplyModifiedProperties();
			};
		}

		// Sets up the reorderable list for delaylessInstructions
		void SetupDelaylessInstructions()
		{
			delaylessList = new ReorderableList(serializedObject, delaylessInstructions, true, true, true, true);

			delaylessList.drawHeaderCallback = (Rect rect) =>
			{
				EditorGUI.LabelField(rect, "Instruction types that won't trigger a delay:");		
			};
			delaylessList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
			{
				rect.y += 2;
				rect.height = EditorGUIUtility.singleLineHeight;
				EditorGUI.PropertyField(rect, delaylessInstructions.GetArrayElementAtIndex(index), GUIContent.none);
			};
			delaylessList.onRemoveCallback += (ReorderableList list) =>
			{
				delaylessInstructions.DeleteArrayElementAtIndex(list.index);
			};
			delaylessList.onAddCallback += (ReorderableList list) =>
			{
				delaylessInstructions.arraySize++;
				delaylessInstructions.GetArrayElementAtIndex(delaylessInstructions.arraySize-1).enumValueIndex = 0;
				serializedObject.ApplyModifiedProperties();
			};
		}

		// Sets up the reorderable list for the instruction stack
		void SetupInstructionList()
		{
			instRList = new ReorderableList(serializedObject, instructions, true, true, true, true);

			instRList.drawHeaderCallback = (Rect rect) =>
			{
				EditorGUI.LabelField(rect, "Instruction List (from top to bottom)");
			};
			instRList.drawElementCallback = InstructionDrawer;
			instRList.onReorderCallback += (ReorderableList list) => { safetyForPlaymode.intValue++; ApplyAll(); };
			instRList.onRemoveCallback += (ReorderableList list) =>
			{
				safetyForPlaymode.intValue++;
				
				//ShotParams sp = instructions.GetArrayElementAtIndex(list.index).FindPropertyRelative("shot").objectReferenceValue as ShotParams;
				//if (sp != null) SetParent(sp, null);
				EmitterProfileUtility.SetParentOfShot(pp.instructionLists[0].instructions[list.index].shot, null, this);

				instructions.DeleteArrayElementAtIndex(list.index);
				ApplyAll();
			};
			instRList.onAddCallback += SetupNewInstruction;
			
			// The sheer existence of SetRect and SetBounds messes with UI, hinders its performance, and nobody will ever need them 
			/* *
			instRList.elementHeightCallback += (int idx) =>
			{
				SerializedProperty inst = instructions.GetArrayElementAtIndex(idx);
				
				float numberOfLines = 1;
				float offset = 0;

				PatternInstructionType instType = (PatternInstructionType)inst.FindPropertyRelative("instructionType").enumValueIndex;
				if (instType == PatternInstructionType.SetCustomRect) numberOfLines++;
				if (instType == PatternInstructionType.SetCustomBounds) numberOfLines++;
				
				return instRList.elementHeight * numberOfLines + offset;
			};
			/* */
		}

		// the OnAddCallback from instruction reorderable list
		public void SetupNewInstruction(ReorderableList list)
		{
			safetyForPlaymode.intValue++;
			instructions.arraySize++;
			SerializedProperty inst = instructions.GetArrayElementAtIndex(instructions.arraySize-1);
			SerializedProperty instType = inst.FindPropertyRelative("instructionType");

			// initialization
			inst.FindPropertyRelative("enabled").boolValue = true;
			instType.enumValueIndex = 0;
			
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("waitTime"), 0f, true);
			DynamicParameterUtility.SetFixedObject(inst.FindPropertyRelative("shot"), null, true);
			
			inst.FindPropertyRelative("endless").boolValue = true;
			DynamicParameterUtility.SetFixedInt(inst.FindPropertyRelative("iterations"), 1, true);
			
			// transform
			DynamicParameterUtility.SetFixedVector2(inst.FindPropertyRelative("globalMovement"), Vector2.zero, true);
			DynamicParameterUtility.SetFixedVector2(inst.FindPropertyRelative("localMovement"), Vector2.zero, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("rotation"), 0f, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("speedValue"), 0f, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("scaleValue"), 1f, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("factor"), 1f, true);

			// effect objects (VFX, Audio)
			SerializedProperty audioClipProp = inst.FindPropertyRelative("audioClip");
			DynamicParameterUtility.SetFixedObject(audioClipProp, null, true);
			DynamicParameterUtility.SetObjectNarrowType(audioClipProp, typeof(AudioClip));
			inst.FindPropertyRelative("vfxPlayType").enumValueIndex = (int)VFXPlayType.Default;
			SerializedProperty vfxProp = inst.FindPropertyRelative("vfxToPlay");
			DynamicParameterUtility.SetFixedObject(vfxProp, null, true);
			DynamicParameterUtility.SetObjectNarrowType(vfxProp, typeof(ParticleSystem));
			SerializedProperty vfxIndex = inst.FindPropertyRelative("vfxIndex");
			DynamicParameterUtility.SetFixedInt(vfxIndex, 0, true);
			SerializedProperty vfxTag = inst.FindPropertyRelative("vfxTag");
			DynamicParameterUtility.SetFixedString(vfxTag, "", true);

			// homing
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("turnIntensity"), 1f, true);
			SerializedProperty preferredTarget = inst.FindPropertyRelative("preferredTarget");
			DynamicParameterUtility.SetFixedInt(preferredTarget, (int)PreferredTarget.Oldest, true);
			DynamicParameterUtility.SetEnumType(preferredTarget, typeof(PreferredTarget));

			// tags
			inst.FindPropertyRelative("collisionTagAction").enumValueIndex = (int)CollisionTagAction.Add;
			DynamicParameterUtility.SetFixedString(inst.FindPropertyRelative("collisionTag"), "Player", true);
			inst.FindPropertyRelative("patternControlTarget").enumValueIndex = (int)PatternControlTarget.ThisPattern;
			DynamicParameterUtility.SetFixedString(inst.FindPropertyRelative("patternTag"), "", true);

			// curves
			SerializedProperty newCurveProp = inst.FindPropertyRelative("newCurveValue");
			DynamicParameterUtility.SetFixedAnimationCurve(newCurveProp, AnimationCurve.EaseInOut(0,0,1,1), true);
			newCurveProp.FindPropertyRelative("forceZeroToOne").boolValue = true;
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("newPeriodValue"), 1f, true);
			inst.FindPropertyRelative("newPeriodType").enumValueIndex = (int)CurvePeriodType.FixedValue;
			SerializedProperty newWrapMode = inst.FindPropertyRelative("newWrapMode");
			DynamicParameterUtility.SetFixedInt(newWrapMode, (int)WrapMode.Default, true);
			DynamicParameterUtility.SetEnumType(newWrapMode, typeof(WrapMode));
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("curveRawTime"), 0f, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("curveTime"), 0f, true);

			// graphics
			DynamicParameterUtility.SetFixedColor(inst.FindPropertyRelative("color"), Color.black, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("alpha"), 1f, true);
			DynamicParameterUtility.SetFixedGradient(inst.FindPropertyRelative("gradient"), BulletProExtensions.DefaultGradient(), true);

			// random seed
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("newRandomSeed"), 0f, true);

			// params for MicroActions
			inst.FindPropertyRelative("instructionTiming").enumValueIndex = (int)InstructionTiming.Instantly;
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("instructionDuration"), 1f, true);
			SerializedProperty operationCurve = inst.FindPropertyRelative("operationCurve");
			DynamicParameterUtility.SetFixedAnimationCurve(operationCurve, AnimationCurve.EaseInOut(0,0,1,1), true);
			operationCurve.FindPropertyRelative("forceZeroToOne").boolValue = true;

			// params that depend on instruction type : this is hard-set to match the "Wait" type
			inst.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.None;
			inst.FindPropertyRelative("canBeDoneOverTime").boolValue = false;
			inst.FindPropertyRelative("displayName").stringValue = "Wait";

			// custom params
			inst.FindPropertyRelative("customParamName").stringValue = "_PowerLevel";
			DynamicParameterUtility.SetFixedInt(inst.FindPropertyRelative("customInt"), 0, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("customFloat"), 0f, true);
			DynamicParameterUtility.SetFixedFloat(inst.FindPropertyRelative("customSlider01"), 0f, true);
			DynamicParameterUtility.SetFixedVector2(inst.FindPropertyRelative("customVector2"), Vector2.zero, true);
			DynamicParameterUtility.SetFixedVector3(inst.FindPropertyRelative("customVector3"), Vector3.zero, true);
			DynamicParameterUtility.SetFixedVector4(inst.FindPropertyRelative("customVector4"), Vector4.zero, true);
			DynamicParameterUtility.SetFixedColor(inst.FindPropertyRelative("customColor"), Color.black, true);
			DynamicParameterUtility.SetFixedGradient(inst.FindPropertyRelative("customGradient"), BulletProExtensions.DefaultGradient(), true);
			DynamicParameterUtility.SetFixedBool(inst.FindPropertyRelative("customBool"), false, true);
			DynamicParameterUtility.SetFixedString(inst.FindPropertyRelative("customString"), "", true);
			DynamicParameterUtility.SetFixedAnimationCurve(inst.FindPropertyRelative("customAnimationCurve"), AnimationCurve.EaseInOut(0,0,1,1), true);
			DynamicParameterUtility.SetFixedObject(inst.FindPropertyRelative("customObjectReference"), null, true);
			DynamicParameterUtility.SetFixedRect(inst.FindPropertyRelative("customRect"), Rect.zero, true);

			ApplyAll();
		}

		// (Copied from ShotParamInspector) Apply GUI-friendly color to a gradient texture that does not mess with ReorderableLists
		public Texture2D GradientTex(Color col)
		{
			Texture2D tex = new Texture2D(128, 1);
			for (int i=0; i<tex.width; i++)
			{
				float ratio = 1.0f - (float)i/(float)tex.width;
				tex.SetPixel(i, 0, new Color(col.r, col.g, col.b, col.a*ratio*ratio)); // squared ratio looks cooler
			}
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.Apply();
			return tex;
		}

		// Assigns enum values to strings for instruction list
		public void SetupPossibleInstructions()
		{
			possibleInstructions = new StringToEnumInstruction[]
			{
				new StringToEnumInstruction("Most Used/Shoot", "Shoot", PatternInstructionType.Shoot),
				new StringToEnumInstruction("Most Used/Wait", "Wait", PatternInstructionType.Wait),
				new StringToEnumInstruction("Most Used/Rotate", "Rotate", PatternInstructionType.Rotate),
				new StringToEnumInstruction("Most Used/Begin Loop", "Begin Loop", PatternInstructionType.BeginLoop),
				new StringToEnumInstruction("Most Used/End Loop", "End Loop", PatternInstructionType.EndLoop),
				new StringToEnumInstruction("Most Used/Play Audio", "Play Audio", PatternInstructionType.PlayAudio),
				new StringToEnumInstruction("Most Used/Die", "Die", PatternInstructionType.Die),

				new StringToEnumInstruction("Effects/Shoot", "Shoot", PatternInstructionType.Shoot),
				new StringToEnumInstruction("Effects/Play VFX", "Play VFX", PatternInstructionType.PlayVFX),
				new StringToEnumInstruction("Effects/Stop VFX", "Stop VFX", PatternInstructionType.StopVFX),
				new StringToEnumInstruction("Effects/Play Audio", "Play Audio", PatternInstructionType.PlayAudio),

				new StringToEnumInstruction("Transform/Position/Translate (World)", "Translate (World)", PatternInstructionType.TranslateGlobal),
				new StringToEnumInstruction("Transform/Position/Translate (Local)", "Translate (Local)", PatternInstructionType.TranslateLocal),
				new StringToEnumInstruction("Transform/Position/Set Position (World)", "Set Position (World)", PatternInstructionType.SetWorldPosition),
				new StringToEnumInstruction("Transform/Position/Set Position (Local)", "Set Position (Local)", PatternInstructionType.SetLocalPosition),
				new StringToEnumInstruction("Transform/Position/Set Speed", "Set Speed", PatternInstructionType.SetSpeed),
				new StringToEnumInstruction("Transform/Position/Multiply Speed", "Multiply Speed", PatternInstructionType.MultiplySpeed),

				new StringToEnumInstruction("Transform/Rotation/Rotate", "Rotate", PatternInstructionType.Rotate),
				new StringToEnumInstruction("Transform/Rotation/Set Rotation (World)", "Set Rotation (World)", PatternInstructionType.SetWorldRotation),
				new StringToEnumInstruction("Transform/Rotation/Set Rotation (Local)", "Set Rotation (Local)", PatternInstructionType.SetLocalRotation),
				new StringToEnumInstruction("Transform/Rotation/Set Angular Speed", "Set Angular Speed", PatternInstructionType.SetAngularSpeed),
				new StringToEnumInstruction("Transform/Rotation/Multiply Angular Speed", "Multiply Angular Speed", PatternInstructionType.MultiplyAngularSpeed),

				new StringToEnumInstruction("Transform/Scale/Set Scale", "Set Scale", PatternInstructionType.SetScale),
				new StringToEnumInstruction("Transform/Scale/Multiply Scale", "Multiply Scale", PatternInstructionType.MultiplyScale),

				new StringToEnumInstruction("Transform/Enable Movement", "Enable Movement", PatternInstructionType.EnableMovement),
				new StringToEnumInstruction("Transform/Disable Movement", "Disable Movement", PatternInstructionType.DisableMovement),
				new StringToEnumInstruction("Transform/Attach to Emitter", "Attach To Emitter", PatternInstructionType.AttachToEmitter),
				new StringToEnumInstruction("Transform/Detach from Emitter", "Detach From Emitter", PatternInstructionType.DetachFromEmitter),

				new StringToEnumInstruction("Flow Control/Wait", "Wait", PatternInstructionType.Wait),
				new StringToEnumInstruction("Flow Control/Begin Loop", "Begin Loop", PatternInstructionType.BeginLoop),
				new StringToEnumInstruction("Flow Control/End Loop", "End Loop", PatternInstructionType.EndLoop),
				new StringToEnumInstruction("Flow Control/Set Instruction Delay", "Set Instr. Delay", PatternInstructionType.SetInstructionDelay),
				new StringToEnumInstruction("Flow Control/Play Pattern", "Play Pattern", PatternInstructionType.PlayPattern),
				new StringToEnumInstruction("Flow Control/Pause Pattern", "Pause Pattern", PatternInstructionType.PausePattern),
				new StringToEnumInstruction("Flow Control/Stop Pattern", "Stop Pattern", PatternInstructionType.StopPattern),
				new StringToEnumInstruction("Flow Control/Reboot Pattern", "Reboot Pattern", PatternInstructionType.RebootPattern),
				new StringToEnumInstruction("Flow Control/Die", "Die", PatternInstructionType.Die),

				new StringToEnumInstruction("Homing/Enable Homing", "Enable Homing", PatternInstructionType.EnableHoming),
				new StringToEnumInstruction("Homing/Disable Homing", "Disable Homing", PatternInstructionType.DisableHoming),
				new StringToEnumInstruction("Homing/Turn To Target", "Turn To Target", PatternInstructionType.TurnToTarget),
				new StringToEnumInstruction("Homing/Change Target", "Change Target", PatternInstructionType.ChangeTarget),
				new StringToEnumInstruction("Homing/Set Homing Speed", "Set Homing Speed", PatternInstructionType.SetHomingSpeed),
				new StringToEnumInstruction("Homing/Multiply Homing Speed", "Multiply Homing Speed", PatternInstructionType.MultiplyHomingSpeed),
				new StringToEnumInstruction("Homing/Change Homing Tag", "Change Homing Tag", PatternInstructionType.ChangeHomingTag),

				new StringToEnumInstruction("Collision/Enable Collision", "Enable Collision", PatternInstructionType.EnableCollision),
				new StringToEnumInstruction("Collision/Disable Collision", "Disable Collision", PatternInstructionType.DisableCollision),
				new StringToEnumInstruction("Collision/Change Collision Tag", "Change Collision Tag", PatternInstructionType.ChangeCollisionTag),

				new StringToEnumInstruction("Graphics/Turn Visible", "Turn Visible", PatternInstructionType.TurnVisible),
				new StringToEnumInstruction("Graphics/Turn Invisible", "Turn Invisible", PatternInstructionType.TurnInvisible),
				new StringToEnumInstruction("Graphics/Sprite Animation/Play", "Play Sprite Anim", PatternInstructionType.PlayAnimation),
				new StringToEnumInstruction("Graphics/Sprite Animation/Pause", "Pause Sprite Anim", PatternInstructionType.PauseAnimation),
				new StringToEnumInstruction("Graphics/Sprite Animation/Reboot", "Reboot Sprite Anim", PatternInstructionType.RebootAnimation),
				new StringToEnumInstruction("Graphics/Color/Set Color", "Set Sprite Color", PatternInstructionType.SetColor),
				new StringToEnumInstruction("Graphics/Color/Add Color", "Add Sprite Color", PatternInstructionType.AddColor),
				new StringToEnumInstruction("Graphics/Color/Multiply Color", "Multiply Sprite Color", PatternInstructionType.MultiplyColor),
				new StringToEnumInstruction("Graphics/Color/Overlay Color", "Overlay Sprite Color", PatternInstructionType.OverlayColor),
				new StringToEnumInstruction("Graphics/Alpha/Set Alpha", "Set Sprite Alpha", PatternInstructionType.SetAlpha),
				new StringToEnumInstruction("Graphics/Alpha/Add Alpha", "Add Sprite Alpha", PatternInstructionType.AddAlpha),
				new StringToEnumInstruction("Graphics/Alpha/Multiply Alpha", "Multiply Sprite Alpha", PatternInstructionType.MultiplyAlpha),
				new StringToEnumInstruction("Graphics/Set Lifetime Gradient", "Set Lifetime Gradient", PatternInstructionType.SetLifetimeGradient),
				
				new StringToEnumInstruction("Custom Params/Number/Integer/Set", "Set Custom Int", PatternInstructionType.SetCustomInteger),
				new StringToEnumInstruction("Custom Params/Number/Integer/Add", "Add Custom Int", PatternInstructionType.AddCustomInteger),
				new StringToEnumInstruction("Custom Params/Number/Integer/Multiply", "Multiply Custom Int", PatternInstructionType.MultiplyCustomInteger),
				new StringToEnumInstruction("Custom Params/Number/Float/Set", "Set Custom Float", PatternInstructionType.SetCustomFloat),
				new StringToEnumInstruction("Custom Params/Number/Float/Add", "Add Custom Float", PatternInstructionType.AddCustomFloat),
				new StringToEnumInstruction("Custom Params/Number/Float/Multiply", "Multiply Custom Float", PatternInstructionType.MultiplyCustomFloat),
				new StringToEnumInstruction("Custom Params/Number/Slider 01/Set", "Set Custom Slider 01", PatternInstructionType.SetCustomSlider01),
				new StringToEnumInstruction("Custom Params/Number/Slider 01/Add", "Add Custom Slider 01", PatternInstructionType.AddCustomSlider01),
				new StringToEnumInstruction("Custom Params/Number/Slider 01/Multiply", "Multiply Custom Slider 01", PatternInstructionType.MultiplyCustomSlider01),
				new StringToEnumInstruction("Custom Params/Number/Double/Set", "Set Custom Double", PatternInstructionType.SetCustomDouble),
				new StringToEnumInstruction("Custom Params/Number/Double/Add", "Add Custom Double", PatternInstructionType.AddCustomDouble),
				new StringToEnumInstruction("Custom Params/Number/Double/Multiply", "Multiply Custom Double", PatternInstructionType.MultiplyCustomDouble),
				new StringToEnumInstruction("Custom Params/Number/Long/Set", "Set Custom Long", PatternInstructionType.SetCustomLong),
				new StringToEnumInstruction("Custom Params/Number/Long/Add", "Add Custom Long", PatternInstructionType.AddCustomLong),
				new StringToEnumInstruction("Custom Params/Number/Long/Multiply", "Multiply Custom Long", PatternInstructionType.MultiplyCustomLong),
				
				new StringToEnumInstruction("Custom Params/Vector/Vector2/Set", "Set Custom Vector2", PatternInstructionType.SetCustomVector2),
				new StringToEnumInstruction("Custom Params/Vector/Vector2/Add", "Add Custom Vector2", PatternInstructionType.AddCustomVector2),
				new StringToEnumInstruction("Custom Params/Vector/Vector2/Multiply", "Multiply Custom Vector2", PatternInstructionType.MultiplyCustomVector2),
				new StringToEnumInstruction("Custom Params/Vector/Vector3/Set", "Set Custom Vector3", PatternInstructionType.SetCustomVector3),
				new StringToEnumInstruction("Custom Params/Vector/Vector3/Add", "Add Custom Vector3", PatternInstructionType.AddCustomVector3),
				new StringToEnumInstruction("Custom Params/Vector/Vector3/Multiply", "Multiply Custom Vector3", PatternInstructionType.MultiplyCustomVector3),
				new StringToEnumInstruction("Custom Params/Vector/Vector4/Set", "Set Custom Vector4", PatternInstructionType.SetCustomVector4),
				new StringToEnumInstruction("Custom Params/Vector/Vector4/Add", "Add Custom Vector4", PatternInstructionType.AddCustomVector4),
				new StringToEnumInstruction("Custom Params/Vector/Vector4/Multiply", "Multiply Custom Vector4", PatternInstructionType.MultiplyCustomVector4),

				new StringToEnumInstruction("Custom Params/Color/Set", "Set Custom Color", PatternInstructionType.SetCustomColor),
				new StringToEnumInstruction("Custom Params/Color/Add", "Add Custom Color", PatternInstructionType.AddCustomColor),
				new StringToEnumInstruction("Custom Params/Color/Multiply", "Multiply Custom Color", PatternInstructionType.MultiplyCustomColor),
				new StringToEnumInstruction("Custom Params/Color/Overlay", "Overlay Custom Color", PatternInstructionType.OverlayCustomColor),

				new StringToEnumInstruction("Custom Params/String/Set", "Set Custom String", PatternInstructionType.SetCustomString),
				new StringToEnumInstruction("Custom Params/String/Append", "Append To Custom String", PatternInstructionType.AppendToCustomString),

				new StringToEnumInstruction("Custom Params/Other/Set Bool", "Set Custom Bool", PatternInstructionType.SetCustomBool),
				new StringToEnumInstruction("Custom Params/Other/Set Animation Curve", "Set Custom Animation Curve", PatternInstructionType.SetCustomAnimationCurve),
				new StringToEnumInstruction("Custom Params/Other/Set Gradient", "Set Custom Gradient", PatternInstructionType.SetCustomGradient),
				new StringToEnumInstruction("Custom Params/Other/Set Quaternion", "Set Custom Quaternion", PatternInstructionType.SetCustomQuaternion),
				new StringToEnumInstruction("Custom Params/Other/Set Object", "Set Custom Object", PatternInstructionType.SetCustomObject),
				// The sheer existence of SetRect and SetBounds messes with the UI, hinders its performance, and nobody will ever need them				
				//new StringToEnumInstruction("Custom Params/Other/Set Rect", "Set Custom Rect", PatternInstructionType.SetCustomRect),
				//new StringToEnumInstruction("Custom Params/Other/Set Bounds", "Set Custom Bounds", PatternInstructionType.SetCustomBounds)

				new StringToEnumInstruction("Random Seed/Freeze", "Freeze Rndm Seed", PatternInstructionType.FreezeRandomSeed),
				new StringToEnumInstruction("Random Seed/Unfreeze", "Unfreeze R. Seed", PatternInstructionType.UnfreezeRandomSeed),
				new StringToEnumInstruction("Random Seed/Reroll", "Reroll Rndm Seed", PatternInstructionType.RerollRandomSeed),
				new StringToEnumInstruction("Random Seed/Manual Set", "Set Random Seed", PatternInstructionType.SetRandomSeed)
			};

			possibleCurveInstructions = new StringToEnumInstruction[]
			{
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Enable", "Enable $curveDisplayName Curve", PatternInstructionType.EnableCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Disable", "Disable $curveDisplayName Curve", PatternInstructionType.DisableCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Play", "Play $curveDisplayName Curve", PatternInstructionType.PlayCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Pause", "Pause $curveDisplayName Curve", PatternInstructionType.PauseCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Rewind", "Rewind $curveDisplayName Curve", PatternInstructionType.RewindCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Reset", "Reset $curveDisplayName Curve", PatternInstructionType.ResetCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Controls/Stop", "Stop $curveDisplayName Curve", PatternInstructionType.StopCurve),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Set Curve", "Set $curveDisplayName Curve", PatternInstructionType.SetCurveValue),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Set WrapMode", "Set $curveDisplayName WrapMode", PatternInstructionType.SetWrapMode),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Set Period", "Set $curveDisplayName Period", PatternInstructionType.SetPeriod),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Multiply Period", "Multiply $curveDisplayName Period", PatternInstructionType.MultiplyPeriod),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Set Raw Time", "Set $curveDisplayName Curve Time", PatternInstructionType.SetCurveRawTime),
				new StringToEnumInstruction("Curves/$curveMenuName/Values/Set Time Ratio", "Set $curveDisplayName Curve Ratio", PatternInstructionType.SetCurveRatio)
			};
		}

		// Builds the GenericMenu for enum-style long list of functions
		public void SetupGenericMenu()
		{
			genericMenu = new GenericMenu();
			
			for (int i = 0; i < possibleInstructions.Length; i++)
				genericMenu.AddItem(new GUIContent(possibleInstructions[i].menuName), false, ValidateDropdownOption, possibleInstructions[i]);

			AddCurveToGenericMenu("Speed Over Lifetime", "Speed", ValidateSpeedCurve);
			AddCurveToGenericMenu("Angular Speed Over Lifetime", "Ang. Speed", ValidateAngSpeedCurve);
			AddCurveToGenericMenu("Scale Over Lifetime", "Scale", ValidateAnimScaleCurve);
			AddCurveToGenericMenu("Homing Over Lifetime", "Homing", ValidateHomingCurve);
			AddCurveToGenericMenu("Color Over Lifetime", "Color", ValidateColorCurve);
			AddCurveToGenericMenu("Alpha Over Lifetime", "Alpha", ValidateAlphaCurve);
			AddCurveToGenericMenu("Animation Clip X", "Anim. X", ValidateAnimXCurve);
			AddCurveToGenericMenu("Animation Clip Y", "Anim. Y", ValidateAnimYCurve);
			AddCurveToGenericMenu("Animation Clip Angle", "Anim. Angle", ValidateAnimAngleCurve);
			AddCurveToGenericMenu("Animation Clip Scale", "Anim. Scale", ValidateAnimScaleCurve);
		}

		void AddCurveToGenericMenu(string curveMenuName, string curveDisplayName, GenericMenu.MenuFunction2 callback)
		{
			for (int i = 0; i < possibleCurveInstructions.Length; i++)
			{
				StringToEnumInstruction stei = new StringToEnumInstruction(
					possibleCurveInstructions[i].menuName.Replace("$curveMenuName", curveMenuName),
					possibleCurveInstructions[i].displayName.Replace("$curveDisplayName", curveDisplayName),
					possibleCurveInstructions[i].enumValue
				);

				genericMenu.AddItem(new GUIContent(stei.menuName), false, callback, stei);
			}
		}

		#region generic menu validation callback

		void ValidateDropdownOption(object obj)
		{
			StringToEnumInstruction stei = obj as StringToEnumInstruction;
			
			SerializedProperty instType = instPropOfOpenedMenu.FindPropertyRelative("instructionType");
			instType.enumValueIndex = (int)stei.enumValue;
			instPropOfOpenedMenu.FindPropertyRelative("displayName").stringValue = stei.displayName;
			instPropOfOpenedMenu.FindPropertyRelative("canBeDoneOverTime").boolValue = CanBeMadeIntroMicroAction(stei.enumValue);
			safetyForPlaymode.intValue++;
			serializedObject.ApplyModifiedProperties();
			
			if (instPropOfOpenedMenu.FindPropertyRelative("enabled").boolValue)
			{
				PatternParams newParent = (instType.enumValueIndex == (int)PatternInstructionType.Shoot) ? pp : null;
				EmitterProfileUtility.SetParentOfShot(pp.instructionLists[0].instructions[indexOfOpenedMenu].shot, newParent, this);
			}
		}

		void ValidateSpeedCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.Speed;
			ValidateDropdownOption(obj);
		}
		void ValidateAngSpeedCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.AngularSpeed;
			ValidateDropdownOption(obj);
		}
		void ValidateScaleCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.Scale;
			ValidateDropdownOption(obj);
		}
		void ValidateHomingCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.Homing;
			ValidateDropdownOption(obj);
		}
		void ValidateColorCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.Color;
			ValidateDropdownOption(obj);
		}
		void ValidateAlphaCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.Alpha;
			ValidateDropdownOption(obj);
		}
		void ValidateAnimXCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.AnimX;
			ValidateDropdownOption(obj);
		}
		void ValidateAnimYCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.AnimY;
			ValidateDropdownOption(obj);
		}
		void ValidateAnimAngleCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.AnimAngle;
			ValidateDropdownOption(obj);
		}
		void ValidateAnimScaleCurve(object obj)
		{
			instPropOfOpenedMenu.FindPropertyRelative("curveAffected").enumValueIndex = (int)PatternCurveType.AnimScale;
			ValidateDropdownOption(obj);
		}

		#endregion

		#endregion

		#region static toolbox for checking InstructionTypes

		public static bool CanBeMadeIntroMicroAction(PatternInstructionType instType)
		{
			int i = (int)instType;

			// Transform : position, rotation, scale
			if ((i >= (int)PatternInstructionType.TranslateGlobal)
				&& (i <= (int)PatternInstructionType.MultiplyScale))
				return true;

			// Homing
			if (instType == PatternInstructionType.TurnToTarget) return true;
			if (instType == PatternInstructionType.SetHomingSpeed) return true;
			if (instType == PatternInstructionType.MultiplyHomingSpeed) return true;

			// Curves
			if ((i >= (int)PatternInstructionType.SetCurveValue)
				&& (i != (int)PatternInstructionType.SetWrapMode)
				&& (i <= (int)PatternInstructionType.SetCurveRatio))
				return true;

			// Colors
			if ((i >= (int)PatternInstructionType.SetColor)
				&& (i <= (int)PatternInstructionType.SetLifetimeGradient))
				return true;

			// Custom parameters
			if ((i >= (int)PatternInstructionType.SetCustomInteger)
				&& (i < (int)PatternInstructionType.SetCustomGradient)) // for custom params, gradients are unspported as MicroActions 
				return true;

			// The rest
			return false;
		}

		public static bool IsMultiplyInstruction(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.MultiplySpeed) return true;
			if (instType == PatternInstructionType.MultiplyAngularSpeed) return true;
			if (instType == PatternInstructionType.MultiplyScale) return true;
			if (instType == PatternInstructionType.MultiplyHomingSpeed) return true;
			if (instType == PatternInstructionType.MultiplyPeriod) return true;
			if (instType == PatternInstructionType.MultiplyAlpha) return true;

			return false;
		}

		public static bool IsSetSpeedInstruction(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.SetSpeed) return true;
			if (instType == PatternInstructionType.SetHomingSpeed) return true;
			if (instType == PatternInstructionType.SetAngularSpeed) return true;

			return false;
		}

		public static bool IsRotationInstruction(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.Rotate) return true;
			if (instType == PatternInstructionType.SetLocalRotation) return true;
			if (instType == PatternInstructionType.SetWorldRotation) return true;

			return false;
		}

		public static bool PromptsForSingleFloat(PatternInstructionType instType)
		{
			if (IsMultiplyInstruction(instType)) return true;
			if (IsSetSpeedInstruction(instType)) return true;
			if (IsRotationInstruction(instType)) return true;

			if (instType == PatternInstructionType.Wait) return true;
			if (instType == PatternInstructionType.SetScale) return true;
			if (instType == PatternInstructionType.SetAlpha) return true;
			if (instType == PatternInstructionType.AddAlpha) return true;
			if (instType == PatternInstructionType.SetCurveRawTime) return true;
			if (instType == PatternInstructionType.SetRandomSeed) return true;
			if (instType == PatternInstructionType.SetInstructionDelay) return true;

			return false;
		}

		public static bool PromptsForSingleVector2(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.TranslateGlobal) return true;
			if (instType == PatternInstructionType.TranslateLocal) return true;
			if (instType == PatternInstructionType.SetWorldPosition) return true;
			if (instType == PatternInstructionType.SetLocalPosition) return true;
			if (instType == PatternInstructionType.SetWorldRotation) return true;

			return false;
		}

		public static bool PromptsForSingleProperty(PatternInstructionType instType)
		{
			if (PromptsForSingleFloat(instType)) return true;
			if (PromptsForSingleColor(instType)) return true;
			if (PromptsForSingleVector2(instType)) return true;
			if (instType == PatternInstructionType.PlayAudio) return true;
			if (instType == PatternInstructionType.SetCurveRatio) return true;
			if (instType == PatternInstructionType.TurnToTarget) return true;
			if (instType == PatternInstructionType.ChangeTarget) return true;
			if (instType == PatternInstructionType.SetWrapMode) return true;
			if (instType == PatternInstructionType.SetLifetimeGradient) return true;
			if (instType == PatternInstructionType.PlayPattern) return true;

			return false;
		}

		public static bool PromptsForSingleColor(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.SetColor) return true;
			if (instType == PatternInstructionType.AddColor) return true;
			if (instType == PatternInstructionType.MultiplyColor) return true;
			if (instType == PatternInstructionType.OverlayColor) return true;

			return false;
		}

		public static bool PromptsForEnumPlusRect(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.BeginLoop) return true;
			if (instType == PatternInstructionType.PlayVFX) return true;
			if (instType == PatternInstructionType.StopVFX) return true;
			if (instType == PatternInstructionType.ChangeHomingTag) return true;
			if (instType == PatternInstructionType.ChangeCollisionTag) return true;
			if (instType == PatternInstructionType.SetPeriod) return true;
			if (instType == PatternInstructionType.PausePattern) return true;
			if (instType == PatternInstructionType.RebootPattern) return true;
			if (instType == PatternInstructionType.StopPattern) return true;

			return false;
		}

		public static bool PromptsForCustomParam(PatternInstructionType instType)
		{
			int i = (int)instType;

			if ((i >= (int)PatternInstructionType.SetCustomInteger)
				&& (i <= (int)PatternInstructionType.SetCustomBounds))
				return true;

			else return false;
		}

		public static string GetRelevantPropertyForCustomParam(PatternInstructionType instType)
		{
			if (instType == PatternInstructionType.SetCustomInteger
				|| instType == PatternInstructionType.AddCustomInteger
				|| instType == PatternInstructionType.MultiplyCustomInteger)
				return "customInt";
			else if (instType == PatternInstructionType.SetCustomFloat
				|| instType == PatternInstructionType.AddCustomFloat
				|| instType == PatternInstructionType.MultiplyCustomFloat)
				return "customFloat";
			else if (instType == PatternInstructionType.MultiplyCustomVector2
				|| instType == PatternInstructionType.MultiplyCustomVector3
				|| instType == PatternInstructionType.MultiplyCustomVector4
				|| instType == PatternInstructionType.MultiplyCustomSlider01)
				return "factor";
			else if (instType == PatternInstructionType.SetCustomSlider01
				|| instType == PatternInstructionType.AddCustomSlider01)
				//|| instType == PatternInstructionType.MultiplyCustomSlider01)
				return "customSlider01";
			else if (instType == PatternInstructionType.SetCustomDouble
				|| instType == PatternInstructionType.AddCustomDouble
				|| instType == PatternInstructionType.MultiplyCustomDouble)
				return "customDouble";
			else if (instType == PatternInstructionType.SetCustomLong
				|| instType == PatternInstructionType.AddCustomLong
				|| instType == PatternInstructionType.MultiplyCustomLong)
				return "customLong";
			else if (instType == PatternInstructionType.SetCustomVector2
				|| instType == PatternInstructionType.AddCustomVector2)
				//|| instType == PatternInstructionType.MultiplyCustomVector2)
				return "customVector2";
			else if (instType == PatternInstructionType.SetCustomVector3
				|| instType == PatternInstructionType.AddCustomVector3)
				//|| instType == PatternInstructionType.MultiplyCustomVector3)
				return "customVector3";
			else if (instType == PatternInstructionType.SetCustomVector4
				|| instType == PatternInstructionType.AddCustomVector4)
				//|| instType == PatternInstructionType.MultiplyCustomVector4)
				return "customVector4";
			else if (instType == PatternInstructionType.SetCustomColor
				|| instType == PatternInstructionType.AddCustomColor
				|| instType == PatternInstructionType.OverlayCustomColor
				|| instType == PatternInstructionType.MultiplyCustomColor)
				return "customColor";
			else if (instType == PatternInstructionType.SetCustomGradient)
				return "customGradient";
			else if (instType == PatternInstructionType.SetCustomBool)
				return "customBool";
			else if (instType == PatternInstructionType.SetCustomString
				|| instType == PatternInstructionType.AppendToCustomString)
				return "customString";
			else if (instType == PatternInstructionType.SetCustomAnimationCurve)
				return "customAnimationCurve";
			else if (instType == PatternInstructionType.SetCustomObject)
				return "customObjectReference";
			else if (instType == PatternInstructionType.SetCustomQuaternion)
				return "customQuaternion";
			
			// Cut for ensuring UI consistency, can be restored if a user ever needs it
			else if (instType == PatternInstructionType.SetCustomRect)
				return "customRect";
			else if (instType == PatternInstructionType.SetCustomBounds)
				return "customBounds";

			Debug.LogError("BulletPro Error: invalid Instruction Type.");
			return "";
		}

		#endregion
	}
}