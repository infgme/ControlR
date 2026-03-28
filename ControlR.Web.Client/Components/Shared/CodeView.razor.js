/**
 * Gets the text content of a DOM element. 
 * @param {HTMLElement} element
 */
export function getElementText(element) {
  if (!element) {
    return null;
  }

  return element.textContent;
}

/**
 * 
 * @param {HTMLElement} element 
 */
export function scrollContainerToBottom(element) {
  if (!element) {
    console.warn("scrollContainerToBottom called with null element reference.");
    return;
  }

  const container = element.closest("div");
  if (!container) {
    console.warn("scrollContainerToBottom: Could not find parent container for code element.");
    return;
  }
  
  container.scrollTo({
    top: container.scrollHeight,
    behavior: "auto"
  });
}