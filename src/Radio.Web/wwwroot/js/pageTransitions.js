// Page transition utilities for Radio Console UI
window.pageTransitions = {
  /**
   * Initialize page transitions by listening to navigation events
   */
  initialize: function() {
    // Listen for Blazor navigation events
    Blazor.addEventListener('enhancedload', () => {
      const content = document.querySelector('.page-transition');
      if (content) {
        // Trigger animation by adding and removing a class
        content.classList.remove('page-transition-active');
        // Force reflow
        void content.offsetWidth;
        content.classList.add('page-transition-active');
      }
    });
  },

  /**
   * Trigger a page transition manually
   */
  triggerTransition: function() {
    const content = document.querySelector('.page-transition');
    if (content) {
      content.classList.remove('page-transition-active');
      void content.offsetWidth; // Force reflow
      content.classList.add('page-transition-active');
      
      // Remove the class after animation completes
      setTimeout(() => {
        content.classList.remove('page-transition-active');
      }, 250);
    }
  }
};

// Auto-initialize when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    window.pageTransitions.initialize();
  });
} else {
  window.pageTransitions.initialize();
}
