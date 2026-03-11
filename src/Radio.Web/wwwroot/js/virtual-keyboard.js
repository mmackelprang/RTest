// Virtual Keyboard for touchscreen path entry
// Lightweight implementation without external dependencies

class VirtualKeyboard {
  constructor() {
    this.isVisible = false;
    this.currentInput = null;
    this.keyboardElement = null;
    this.capsLock = false;
    this.currentMode = 'qwerty'; // 'qwerty' or 'numeric'
  }

  initialize() {
    // Create keyboard HTML
    this.keyboardElement = document.createElement('div');
    this.keyboardElement.id = 'virtual-keyboard';
    this.keyboardElement.className = 'virtual-keyboard-container';
    this.keyboardElement.style.display = 'none';

    this.keyboardElement.innerHTML = this.getKeyboardHTML('qwerty');
    document.body.appendChild(this.keyboardElement);

    // Attach event listeners on the outer container (delegates to inner content)
    this.attachEventListeners();

    // Auto-show keyboard when any text-like input receives focus (kiosk has no physical keyboard)
    document.addEventListener('focusin', (e) => {
      const input = e.target;
      if (input.tagName === 'TEXTAREA') {
        this.show(input);
        return;
      }
      if (input.tagName === 'INPUT') {
        // Skip inputs that are part of dropdown/select components (they open their own popover)
        if (input.closest('.rz-dropdown, .rz-autocomplete')) return;
        // Skip hidden, checkbox, radio, file, color, range, button-like inputs
        const type = (input.type || 'text').toLowerCase();
        const textTypes = ['text', 'search', 'email', 'password', 'url', 'tel', 'number'];
        if (textTypes.includes(type)) {
          this.show(input);
        }
      }
    });

    // Auto-hide when focus leaves all inputs (but not when clicking keyboard keys)
    document.addEventListener('focusout', (e) => {
      setTimeout(() => {
        const active = document.activeElement;
        if (!active ||
            (active.tagName !== 'INPUT' && active.tagName !== 'TEXTAREA' &&
             !active.closest('#virtual-keyboard'))) {
          if (this.isVisible && !this.keyboardElement.contains(document.activeElement)) {
            this.hide();
          }
        }
      }, 100);
    });
  }

  getKeyboardHTML(mode) {
    if (mode === 'numeric') return this.getNumericHTML();
    return this.getQwertyHTML();
  }

  getQwertyHTML() {
    return `
      <div class="virtual-keyboard">
        <div class="keyboard-header">
          <span class="keyboard-title">Virtual Keyboard</span>
          <button class="keyboard-close" data-action="close">✕</button>
        </div>
        <div class="keyboard-keys">
          <!-- Number Row -->
          <div class="keyboard-row">
            <button class="key" data-key="1">1</button>
            <button class="key" data-key="2">2</button>
            <button class="key" data-key="3">3</button>
            <button class="key" data-key="4">4</button>
            <button class="key" data-key="5">5</button>
            <button class="key" data-key="6">6</button>
            <button class="key" data-key="7">7</button>
            <button class="key" data-key="8">8</button>
            <button class="key" data-key="9">9</button>
            <button class="key" data-key="0">0</button>
            <button class="key key-backspace" data-action="backspace">⌫</button>
          </div>

          <!-- Top Row -->
          <div class="keyboard-row">
            <button class="key" data-key="q">q</button>
            <button class="key" data-key="w">w</button>
            <button class="key" data-key="e">e</button>
            <button class="key" data-key="r">r</button>
            <button class="key" data-key="t">t</button>
            <button class="key" data-key="y">y</button>
            <button class="key" data-key="u">u</button>
            <button class="key" data-key="i">i</button>
            <button class="key" data-key="o">o</button>
            <button class="key" data-key="p">p</button>
          </div>

          <!-- Middle Row -->
          <div class="keyboard-row">
            <button class="key" data-key="a">a</button>
            <button class="key" data-key="s">s</button>
            <button class="key" data-key="d">d</button>
            <button class="key" data-key="f">f</button>
            <button class="key" data-key="g">g</button>
            <button class="key" data-key="h">h</button>
            <button class="key" data-key="j">j</button>
            <button class="key" data-key="k">k</button>
            <button class="key" data-key="l">l</button>
          </div>

          <!-- Bottom Row -->
          <div class="keyboard-row">
            <button class="key key-shift" data-action="shift">⇧ Shift</button>
            <button class="key" data-key="z">z</button>
            <button class="key" data-key="x">x</button>
            <button class="key" data-key="c">c</button>
            <button class="key" data-key="v">v</button>
            <button class="key" data-key="b">b</button>
            <button class="key" data-key="n">n</button>
            <button class="key" data-key="m">m</button>
            <button class="key" data-key="-">-</button>
            <button class="key" data-key="_">_</button>
          </div>

          <!-- Special Characters Row -->
          <div class="keyboard-row">
            <button class="key" data-key="/">/ </button>
            <button class="key" data-key="\\">\\ </button>
            <button class="key" data-key=":">:</button>
            <button class="key" data-key=".">.</button>
            <button class="key" data-key="@">@</button>
            <button class="key key-space" data-key=" ">Space</button>
            <button class="key" data-key="$">$</button>
            <button class="key" data-key="#">#</button>
            <button class="key key-enter" data-action="enter">Enter</button>
          </div>
        </div>
      </div>
    `;
  }

  getNumericHTML() {
    return `
      <div class="virtual-keyboard numpad">
        <div class="keyboard-header">
          <span class="keyboard-title">Numpad</span>
          <button class="keyboard-close" data-action="close">✕</button>
        </div>
        <div class="keyboard-keys numpad-keys">
          <div class="keyboard-row">
            <button class="key numpad-key" data-key="7">7</button>
            <button class="key numpad-key" data-key="8">8</button>
            <button class="key numpad-key" data-key="9">9</button>
            <button class="key numpad-key key-backspace" data-action="backspace">⌫</button>
          </div>
          <div class="keyboard-row">
            <button class="key numpad-key" data-key="4">4</button>
            <button class="key numpad-key" data-key="5">5</button>
            <button class="key numpad-key" data-key="6">6</button>
            <button class="key numpad-key" data-key=".">.</button>
          </div>
          <div class="keyboard-row">
            <button class="key numpad-key" data-key="1">1</button>
            <button class="key numpad-key" data-key="2">2</button>
            <button class="key numpad-key" data-key="3">3</button>
            <button class="key numpad-key" data-key="-">-</button>
          </div>
          <div class="keyboard-row">
            <button class="key numpad-key numpad-zero" data-key="0">0</button>
            <button class="key numpad-key" data-key="00">00</button>
            <button class="key numpad-key key-enter" data-action="enter">Enter</button>
          </div>
        </div>
      </div>
    `;
  }

  attachEventListeners() {
    // Click handler for all keys
    this.keyboardElement.addEventListener('click', (e) => {
      const button = e.target.closest('button');
      if (!button) return;
      
      const key = button.dataset.key;
      const action = button.dataset.action;
      
      if (action) {
        this.handleAction(action);
      } else if (key) {
        this.handleKeyPress(key);
      }
    });
    
    // Prevent keyboard clicks from stealing focus from the active text input.
    // preventDefault on ALL mousedown (including buttons) keeps focus on the input;
    // the click handler still fires and dispatches key presses.
    this.keyboardElement.addEventListener('mousedown', (e) => {
      e.preventDefault();
      e.stopPropagation();
    });

    // Same for touchstart — prevent focus steal on touchscreen taps
    this.keyboardElement.addEventListener('touchstart', (e) => {
      // Don't preventDefault here (breaks touch click), but stop propagation
      e.stopPropagation();
    });
  }

  handleKeyPress(key) {
    if (!this.currentInput) return;
    
    const input = this.currentInput;
    const start = input.selectionStart;
    const end = input.selectionEnd;
    const value = input.value;
    
    // Apply case transformation if shift/caps is active
    let finalKey = key;
    if (this.capsLock && key.length === 1) {
      finalKey = key.toUpperCase();
    }
    
    // Insert character at cursor position
    input.value = value.substring(0, start) + finalKey + value.substring(end);
    
    // Move cursor after inserted character
    const newPosition = start + finalKey.length;
    input.setSelectionRange(newPosition, newPosition);
    
    // Trigger input event for Blazor binding
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    
    // Focus back on input
    input.focus();
  }

  handleAction(action) {
    switch (action) {
      case 'backspace':
        this.handleBackspace();
        break;
      case 'shift':
        this.handleShift();
        break;
      case 'enter':
        this.handleEnter();
        break;
      case 'close':
        this.hide();
        break;
    }
  }

  handleBackspace() {
    if (!this.currentInput) return;
    
    const input = this.currentInput;
    const start = input.selectionStart;
    const end = input.selectionEnd;
    const value = input.value;
    
    if (start === end && start > 0) {
      // Delete single character before cursor
      input.value = value.substring(0, start - 1) + value.substring(end);
      input.setSelectionRange(start - 1, start - 1);
    } else if (start !== end) {
      // Delete selection
      input.value = value.substring(0, start) + value.substring(end);
      input.setSelectionRange(start, start);
    }
    
    // Trigger events
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    input.focus();
  }

  handleShift() {
    this.capsLock = !this.capsLock;
    const shiftButton = this.keyboardElement.querySelector('[data-action="shift"]');
    
    // NOTE: data-key holds the canonical key value (typically lowercase);
    // we only change the displayed label (textContent) for shift/caps.
    if (this.capsLock) {
      shiftButton.classList.add('active');
      // Update all letter keys to uppercase (case-insensitive detection)
      this.keyboardElement.querySelectorAll('.key[data-key]').forEach(key => {
        const keyValue = key.dataset.key;
        if (keyValue && keyValue.length === 1 && /[a-z]/i.test(keyValue)) {
          key.textContent = keyValue.toUpperCase();
        }
      });
    } else {
      shiftButton.classList.remove('active');
      // Restore all letter keys to lowercase (case-insensitive detection)
      this.keyboardElement.querySelectorAll('.key[data-key]').forEach(key => {
        const keyValue = key.dataset.key;
        if (keyValue && keyValue.length === 1 && /[a-z]/i.test(keyValue)) {
          key.textContent = keyValue.toLowerCase();
        }
      });
    }
  }

  handleEnter() {
    if (!this.currentInput) return;
    
    // Trigger Enter key event
    const enterEvent = new KeyboardEvent('keyup', {
      key: 'Enter',
      code: 'Enter',
      keyCode: 13,
      bubbles: true
    });
    this.currentInput.dispatchEvent(enterEvent);
    
    // Hide keyboard after Enter
    setTimeout(() => this.hide(), 100);
  }

  detectInputMode(inputElement) {
    // Explicit opt-in via data attribute
    const dataKeyboard = inputElement.getAttribute('data-keyboard');
    if (dataKeyboard === 'numeric') return 'numeric';
    if (dataKeyboard === 'qwerty') return 'qwerty';

    // Detect from input type and inputmode attributes
    const type = inputElement.getAttribute('type');
    const inputMode = inputElement.getAttribute('inputmode');

    if (type === 'number' || inputMode === 'numeric' || inputMode === 'decimal') {
      return 'numeric';
    }

    // RadzenNumeric renders an inner <input> with type="number" inside a wrapper
    const wrapper = inputElement.closest('.rz-spinner');
    if (wrapper && wrapper.querySelector('input[type="number"]')) {
      return 'numeric';
    }

    return 'qwerty';
  }

  show(inputElement) {
    this.currentInput = inputElement;

    // Detect desired keyboard mode
    const mode = this.detectInputMode(inputElement);

    // Swap layout if mode changed (event listeners delegate from outer container, no re-attach needed)
    if (mode !== this.currentMode) {
      this.currentMode = mode;
      this.keyboardElement.innerHTML = this.getKeyboardHTML(mode);
    }

    this.keyboardElement.style.display = 'block';
    this.isVisible = true;
    document.body.classList.add('keyboard-active');

    // Add active class for animations
    setTimeout(() => {
      this.keyboardElement.classList.add('active');
    }, 10);
  }

  hide() {
    this.keyboardElement.classList.remove('active');
    document.body.classList.remove('keyboard-active');

    setTimeout(() => {
      this.keyboardElement.style.display = 'none';
      this.isVisible = false;
      this.currentInput = null;
      this.capsLock = false;

      // Reset shift button (only present in qwerty mode)
      const shiftButton = this.keyboardElement.querySelector('[data-action="shift"]');
      if (shiftButton) {
        shiftButton.classList.remove('active');
      }

      // Reset mode so next show() re-evaluates
      this.currentMode = 'qwerty';
      this.keyboardElement.innerHTML = this.getKeyboardHTML('qwerty');
    }, 300);
  }

  toggle(inputElement) {
    if (this.isVisible && this.currentInput === inputElement) {
      this.hide();
    } else {
      this.show(inputElement);
    }
  }
}

// Global instance
window.virtualKeyboard = new VirtualKeyboard();

// Initialize when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    window.virtualKeyboard.initialize();
  });
} else {
  window.virtualKeyboard.initialize();
}

// Export for .NET interop
window.virtualKeyboardInterop = {
  show: (element) => window.virtualKeyboard.show(element),
  hide: () => window.virtualKeyboard.hide(),
  toggle: (element) => window.virtualKeyboard.toggle(element),
  isVisible: () => window.virtualKeyboard.isVisible,
  toggleForInput: (selector) => {
    const input = document.querySelector(selector);
    if (input && window.virtualKeyboard) {
      window.virtualKeyboard.toggle(input);
    }
  }
};

// Export the toggleForInput function for ES module import
export function toggleForInput(selector) {
  const input = document.querySelector(selector);
  if (input && window.virtualKeyboard) {
    window.virtualKeyboard.toggle(input);
  }
}
