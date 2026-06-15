using UnityEngine;
using System.Collections.Generic;

namespace EdgeParty.Utils
{
    /// <summary>
    /// A simple utility script to test animations on any object with an Animator component.
    /// Useful for exploring asset animations without modifying existing controllers.
    /// </summary>
    public class AnimationTester : MonoBehaviour
    {
        [System.Serializable]
        public struct StateMapping
        {
            public string stateName;
            public int animationValue;
        }

        [Header("Settings")]
        [Tooltip("The name of the animation state to play.")]
        public string stateName = "Idle";
        
        [Tooltip("The value to set for the 'animation' integer parameter (common in many assets).")]
        public int animationInt = 0;

        [Header("Mapping (Auto-populated by Editor)")]
        public List<StateMapping> stateMappings = new List<StateMapping>();

        [Header("UI Settings")]
        public bool showGUI = true;
        public Rect guiRect = new Rect(10, 10, 200, 250);

        private Animator animator;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                Debug.LogWarning("[AnimationTester] No Animator found on " + name + " or its children.");
            }
        }

        [ContextMenu("Play State By Name")]
        public void PlayByName()
        {
            if (animator != null)
            {
                // Check if we have a mapping for this state to an 'animation' integer
                foreach (var mapping in stateMappings)
                {
                    if (mapping.stateName == stateName)
                    {
                        animationInt = mapping.animationValue;
                        animator.SetInteger("animation", animationInt);
                        break;
                    }
                }

                animator.Play(stateName);
                Debug.Log($"[AnimationTester] Playing state: {stateName} (animation int: {animationInt})");
            }
        }

        [ContextMenu("Set Animation Integer")]
        public void SetAnimationInt()
        {
            if (animator != null)
            {
                animator.SetInteger("animation", animationInt);
                Debug.Log($"[AnimationTester] Set 'animation' parameter to: {animationInt}");
            }
        }

        private void OnGUI()
        {
            if (!showGUI || animator == null) return;

            guiRect = GUILayout.Window(99, guiRect, DrawWindow, "Animation Tester (" + gameObject.name + ")");
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.Label("State Name:");
            stateName = GUILayout.TextField(stateName);
            if (GUILayout.Button("Play Name")) PlayByName();

            GUILayout.Space(10);

            GUILayout.Label("'animation' Int:");
            string intStr = GUILayout.TextField(animationInt.ToString());
            if (int.TryParse(intStr, out int val))
            {
                animationInt = val;
            }

            if (GUILayout.Button("Set Int")) SetAnimationInt();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Prev"))
            {
                animationInt--;
                SetAnimationInt();
            }
            if (GUILayout.Button("Next >"))
            {
                animationInt++;
                SetAnimationInt();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Stop All"))
            {
                animator.Rebind();
            }

            GUI.DragWindow();
        }
    }
}
