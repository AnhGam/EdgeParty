using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using EdgeParty.Utils;

namespace EdgeParty.Editor
{
    [CustomEditor(typeof(AnimationTester))]
    public class AnimationTesterEditor : UnityEditor.Editor
    {
        private List<string> stateNames = new List<string>();
        private Vector2 scrollPos;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            AnimationTester tester = (AnimationTester)target;
            Animator animator = tester.GetComponent<Animator>();
            if (animator == null) animator = tester.GetComponentInChildren<Animator>();

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                GUILayout.Space(10);
                if (GUILayout.Button("Fetch All States from Controller"))
                {
                    FetchStates(animator.runtimeAnimatorController);
                }

                if (stateNames.Count > 0)
                {
                    GUILayout.Space(5);
                    GUILayout.Label("Click to Play State:", EditorStyles.boldLabel);
                    
                    scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
                    foreach (string name in stateNames)
                    {
                        if (GUILayout.Button(name, GUILayout.Height(25)))
                        {
                            tester.stateName = name;
                            if (Application.isPlaying)
                            {
                                tester.PlayByName();
                            }
                            else
                            {
                                Debug.LogWarning("You must be in Play Mode to play animations via components.");
                            }
                        }
                    }
                    GUILayout.EndScrollView();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No Animator or AnimatorController found on this object.", MessageType.Info);
            }
        }

        private void FetchStates(RuntimeAnimatorController controller)
        {
            stateNames.Clear();
            AnimationTester tester = (AnimationTester)target;
            tester.stateMappings.Clear();
            
            // Try to cast to AnimatorController (available in Editor)
            AnimatorController ac = controller as AnimatorController;
            
            // If it's an OverrideController, we need the base controller
            if (ac == null && controller is AnimatorOverrideController overrideController)
            {
                ac = overrideController.runtimeAnimatorController as AnimatorController;
            }

            if (ac == null)
            {
                Debug.LogError("Could not cast AnimatorController. Is it a valid Controller asset?");
                return;
            }

            foreach (var layer in ac.layers)
            {
                ChildAnimatorState[] states = layer.stateMachine.states;
                foreach (var childState in states)
                {
                    string name = childState.state.name;
                    if (!stateNames.Contains(name))
                        stateNames.Add(name);

                    // Try to find a transition that sets the 'animation' int for this state
                    // We look at all transitions in the state machine
                    DiscoverMapping(layer.stateMachine, childState.state, tester);
                }
            }
            
            stateNames.Sort();
            EditorUtility.SetDirty(tester);
        }

        private void DiscoverMapping(AnimatorStateMachine sm, AnimatorState targetState, AnimationTester tester)
        {
            // Check transitions coming into this state from the state machine
            foreach (var transition in sm.anyStateTransitions)
            {
                if (transition.destinationState == targetState)
                {
                    CheckConditions(transition, targetState.name, tester);
                }
            }

            // Check transitions from other states (especially the selector state)
            foreach (var childState in sm.states)
            {
                foreach (var transition in childState.state.transitions)
                {
                    if (transition.destinationState == targetState)
                    {
                        CheckConditions(transition, targetState.name, tester);
                    }
                }
            }
            
            // Also check sub-state machines recursively if needed, but for Suriyun mostly flat
        }

        private void CheckConditions(AnimatorStateTransition transition, string stateName, AnimationTester tester)
        {
            foreach (var condition in transition.conditions)
            {
                if (condition.parameter == "animation" && condition.mode == AnimatorConditionMode.Equals)
                {
                    // Found a mapping!
                    int val = (int)condition.threshold;
                    
                    // Add to tester mappings if not already there
                    bool exists = false;
                    foreach (var m in tester.stateMappings)
                    {
                        if (m.stateName == stateName) { exists = true; break; }
                    }
                    
                    if (!exists)
                    {
                        tester.stateMappings.Add(new AnimationTester.StateMapping { 
                            stateName = stateName, 
                            animationValue = val 
                        });
                    }
                }
            }
        }
    }
}
