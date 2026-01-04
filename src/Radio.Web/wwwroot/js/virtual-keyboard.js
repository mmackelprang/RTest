// Virtual Keyboard for touchscreen path entry
// Lightweight implementation without external dependencies

class VirtualKeyboard {
  constructor() {
    this.isVisible = false;
    this.currentInput = null;
    this.keyboardElement = null;
    this.capsLock = false;
  }

  initialize() {
    // Create keyboard HTML
    this.keyboardElement = document.createElement('div');
    this.keyboardElement.id = 'virtual-keyboard';
    this.keyboardElement.className = 'virtual-keyboard-container';
    this.keyboardElement.style.display = 'none';
    
    this.keyboardElement.innerHTML = this.getKeyboardHTML();
    document.body.appendChild(this.keyboardElement);
    
    // Add event listeners
    this.attachEventListeners();
  }

  getKeyboardHTML() {
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
    
    // Prevent keyboard from closing when clicking inside
    this.keyboardElement.addEventListener('mousedown', (e) => {
      e.preventDefault();
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
    
    if (this.capsLock) {
      shiftButton.classList.add('active');
      // Update all letter keys to uppercase
      this.keyboardElement.querySelectorAll('.key[data-key]').forEach(key => {
        const keyValue = key.dataset.key;
        if (keyValue && keyValue.length === 1 && keyValue.match(/[a-z]/)) {
          key.textContent = keyValue.toUpperCase();
        }
      });
    } else {
      shiftButton.classList.remove('active');
      // Restore lowercase
      this.keyboardElement.querySelectorAll('.key[data-key]').forEach(key => {
        const keyValue = key.dataset.key;
        if (keyValue && keyValue.length === 1 && keyValue.match(/[a-z]/i)) {
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

  show(inputElement) {
    this.currentInput = inputElement;
    this.keyboardElement.style.display = 'block';
    this.isVisible = true;
    
    // Add active class for animations
    setTimeout(() => {
      this.keyboardElement.classList.add('active');
    }, 10);
  }

  hide() {
    this.keyboardElement.classList.remove('active');
    
    setTimeout(() => {
      this.keyboardElement.style.display = 'none';
      this.isVisible = false;
      this.currentInput = null;
      this.capsLock = false;
      
      // Reset shift button
      const shiftButton = this.keyboardElement.querySelector('[data-action="shift"]');
      if (shiftButton) {
        shiftButton.classList.remove('active');
      }
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
  isVisible: () => window.virtualKeyboard.isVisible
};
