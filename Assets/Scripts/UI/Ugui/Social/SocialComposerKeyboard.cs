using System;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Social
{
    /// <summary>Enter sends; Shift+Enter inserts a newline. A composition commit never sends.</summary>
    public sealed class SocialComposerKeyboard : MonoBehaviour
    {
        public TMP_InputField Field; public Action Send; private bool _composing;private int _compositionEndedFrame=-10;
#if ENABLE_INPUT_SYSTEM
        private Keyboard _keyboard;
        private void OnEnable(){_keyboard=Keyboard.current;if(_keyboard!=null)_keyboard.onIMECompositionChange+=Composition;}
        private void OnDisable(){if(_keyboard!=null)_keyboard.onIMECompositionChange-=Composition;_keyboard=null;_composing=false;}
        private void Composition(IMECompositionString text){bool previous=_composing;_composing=text.Count>0;if(previous&&!_composing)_compositionEndedFrame=Time.frameCount;}
#endif
        public void LateUpdate()
        {
            if(Field==null||!Field.isFocused)return;
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;if(keyboard==null)return;
            bool enter=keyboard.enterKey.wasPressedThisFrame||keyboard.numpadEnterKey.wasPressedThisFrame;
            bool shift=keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            _composing=!string.IsNullOrEmpty(Input.compositionString);
            bool enter=Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter);
            bool shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
#else
            bool enter=false,shift=false;
#endif
            if(!enter||shift||_composing||Time.frameCount<=_compositionEndedFrame+1)return;
            // TMP multi-line input may have inserted Enter before LateUpdate. Remove only that key's newline.
            int caret=Field.stringPosition;string value=Field.text;
            if(caret>0&&caret<=value.Length&&value[caret-1]=='\n'){value=value.Remove(caret-1,1);Field.text=value;Field.stringPosition=caret-1;}
            Send?.Invoke();
        }
    }
}
