// File download utilities for Radio Console UI
window.fileDownload = {
  /**
   * Download text content as a file
   * @param {string} filename - The name of the file to download
   * @param {string} content - The text content to download
   * @param {string} mimeType - The MIME type (default: text/plain)
   */
  downloadTextFile: function(filename, content, mimeType = 'text/plain') {
    try {
      // Create a blob from the content
      const blob = new Blob([content], { type: mimeType });
      
      // Create a temporary URL for the blob
      const url = URL.createObjectURL(blob);
      
      // Create a temporary anchor element
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = filename;
      anchor.style.display = 'none';
      
      // Add to DOM, click, and remove
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      
      // Clean up the URL
      setTimeout(() => URL.revokeObjectURL(url), 100);
      
      return true;
    } catch (error) {
      console.error('Error downloading file:', error);
      return false;
    }
  },

  /**
   * Download JSON data as a file
   * @param {string} filename - The name of the file to download
   * @param {object} data - The data to serialize as JSON
   */
  downloadJsonFile: function(filename, data) {
    try {
      const content = JSON.stringify(data, null, 2);
      return this.downloadTextFile(filename, content, 'application/json');
    } catch (error) {
      console.error('Error downloading JSON file:', error);
      return false;
    }
  },

  /**
   * Download base64 data as a file
   * @param {string} filename - The name of the file to download
   * @param {string} base64Data - The base64-encoded data
   * @param {string} mimeType - The MIME type
   */
  downloadBase64File: function(filename, base64Data, mimeType) {
    try {
      // Convert base64 to bytes
      const byteCharacters = atob(base64Data);
      const byteNumbers = new Array(byteCharacters.length);
      for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
      }
      const byteArray = new Uint8Array(byteNumbers);
      
      // Create blob and download
      const blob = new Blob([byteArray], { type: mimeType });
      const url = URL.createObjectURL(blob);
      
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = filename;
      anchor.style.display = 'none';
      
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      
      setTimeout(() => URL.revokeObjectURL(url), 100);
      
      return true;
    } catch (error) {
      console.error('Error downloading base64 file:', error);
      return false;
    }
  }
};

// Simple wrapper for backwards compatibility
window.downloadFile = function(filename, base64Data) {
  const mimeType = filename.endsWith('.json') ? 'application/json' : 'application/octet-stream';
  return window.fileDownload.downloadBase64File(filename, base64Data, mimeType);
};
