window.cockpit = window.cockpit || {};

/*
 * ContentEditable bridge for the chat input.
 * It keeps DOM-specific behavior in JavaScript while exposing a plain-text model to Blazor.
 * Main concerns:
 * - chip lifecycle and removal notifications
 * - mention trigger tracking while focus moves to the picker
 * - DOM <-> plain-text conversion via #file:"path" tokens
 * - send/newline/backspace/delete behavior around non-editable chips
 */

const zeroWidthSpace = '\u200B';
const chipSpacerText = `${zeroWidthSpace} `;
const chipSelector = '.file-mention-chip';
const chipDeleteSelector = '.chip-delete';
const maxContentEditableHeight = 300;
const svgNamespace = 'http://www.w3.org/2000/svg';
const chipIconPathData = 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z';
const fileChipTokenPattern = /#file:("(?:[^"\\]|\\.)*")/g;
const blockContainerTags = new Set(['DIV', 'P']);
const mentionNavigationKeys = new Set(['Enter', 'ArrowDown', 'ArrowUp', 'Escape']);
const contentEditableStates = new WeakMap();
const chipIconTemplate = createChipIconTemplate();

function noop() {
}

function getContentEditableElement(id) {
    return document.getElementById(id);
}

function isTextNode(node) {
    return node?.nodeType === Node.TEXT_NODE;
}

function isElementNode(node) {
    return node?.nodeType === Node.ELEMENT_NODE;
}

function isFileChipNode(node) {
    return isElementNode(node) && node.classList.contains('file-mention-chip');
}

function isNodeInsideFileChip(node) {
    if (isFileChipNode(node)) {
        return true;
    }

    return isTextNode(node) && node.parentElement?.closest(chipSelector) !== null;
}

function createContentEditableState() {
    return {
        dotnetRef: null,
        savedMentionRange: null,
        chipRemovalNotificationDepth: 0,
        enterToSend: false,
        handlers: Object.create(null),
        observer: null
    };
}

function getContentEditableState(element) {
    let state = contentEditableStates.get(element);
    if (!state) {
        state = createContentEditableState();
        contentEditableStates.set(element, state);
    }

    return state;
}

function clearSavedMentionRange(state) {
    state.savedMentionRange = null;
}

function defer(callback) {
    if (typeof queueMicrotask === 'function') {
        queueMicrotask(callback);
        return;
    }

    setTimeout(callback, 0);
}

function invokeDotNet(state, methodName, ...args) {
    if (!state.dotnetRef) {
        return;
    }

    state.dotnetRef.invokeMethodAsync(methodName, ...args).catch(noop);
}

function addTrackedEventListener(element, state, eventName, handler) {
    removeTrackedEventListener(element, state, eventName);
    element.addEventListener(eventName, handler);
    state.handlers[eventName] = handler;
}

function removeTrackedEventListener(element, state, eventName) {
    const handler = state.handlers[eventName];
    if (!handler) {
        return;
    }

    element.removeEventListener(eventName, handler);
    delete state.handlers[eventName];
}

function disconnectTrackedObserver(state) {
    if (!state.observer) {
        return;
    }

    state.observer.disconnect();
    state.observer = null;
}

function isChipRemovalNotificationSuppressed(state) {
    return state.chipRemovalNotificationDepth > 0;
}

function beginChipRemovalNotificationSuppression(state) {
    state.chipRemovalNotificationDepth += 1;
}

function endChipRemovalNotificationSuppression(state) {
    // MutationObserver callbacks run before the microtask queue drains, so defer the depth
    // decrement until after the current removal batch has been observed.
    defer(function () {
        state.chipRemovalNotificationDepth = Math.max(0, state.chipRemovalNotificationDepth - 1);
    });
}

function withSuppressedChipRemovalNotifications(element, action) {
    const state = getContentEditableState(element);
    beginChipRemovalNotificationSuppression(state);

    try {
        return action();
    } finally {
        endChipRemovalNotificationSuppression(state);
    }
}

function createChipIconTemplate() {
    const icon = document.createElementNS(svgNamespace, 'svg');
    icon.setAttribute('xmlns', svgNamespace);
    icon.setAttribute('width', '12');
    icon.setAttribute('height', '12');
    icon.setAttribute('fill', 'none');
    icon.setAttribute('stroke', 'currentColor');
    icon.setAttribute('viewBox', '0 0 24 24');
    icon.setAttribute('stroke-width', '2');
    icon.setAttribute('stroke-linecap', 'round');
    icon.setAttribute('stroke-linejoin', 'round');

    const iconPath = document.createElementNS(svgNamespace, 'path');
    iconPath.setAttribute('d', chipIconPathData);
    icon.appendChild(iconPath);

    return icon;
}

function createChipIconElement() {
    return chipIconTemplate.cloneNode(true);
}

function createChipSpacerNode() {
    // The zero-width space keeps the caret placeable beside a non-editable chip.
    return document.createTextNode(chipSpacerText);
}

function createFileChipElement(chipId, filePath, fileName) {
    const chip = document.createElement('span');
    chip.className = 'file-mention-chip';
    chip.contentEditable = 'false';
    chip.dataset.chipId = chipId;
    chip.dataset.filePath = filePath;
    chip.title = filePath;

    const nameSpan = document.createElement('span');
    nameSpan.className = 'chip-name';
    nameSpan.textContent = ` ${fileName}`;

    const deleteButton = document.createElement('button');
    deleteButton.className = 'chip-delete';
    deleteButton.tabIndex = -1;
    deleteButton.title = 'Remove';
    deleteButton.textContent = '×';

    chip.append(createChipIconElement(), nameSpan, deleteButton);
    return chip;
}

function appendTextNodeIfNotEmpty(parent, text) {
    if (text.length > 0) {
        parent.appendChild(document.createTextNode(text));
    }
}

function generateChipId() {
    return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : Math.random().toString(36).slice(2);
}

function getFileNameFromPath(filePath) {
    return filePath.split(/[\\/]/).pop() || filePath;
}

function escapeAttributeValue(value) {
    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
        return CSS.escape(value);
    }

    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function findChipById(element, chipId) {
    return element.querySelector(`${chipSelector}[data-chip-id="${escapeAttributeValue(chipId)}"]`);
}

function getLeadingCaretAnchorLength(text) {
    const leadingAnchorMatch = text.match(/^\u200B+/);
    return leadingAnchorMatch ? leadingAnchorMatch[0].length : 0;
}

function consumeLeadingChipSpacer(textNode) {
    const originalText = textNode.textContent || '';
    const trimmedText = originalText.replace(/^\u200B+ ?/, '');
    if (trimmedText === originalText) {
        return false;
    }

    if (trimmedText.length === 0) {
        textNode.remove();
    } else {
        textNode.textContent = trimmedText;
    }

    return true;
}

function removeChipSpacerAfter(chip) {
    let nextSibling = chip.nextSibling;

    while (isTextNode(nextSibling) && consumeLeadingChipSpacer(nextSibling)) {
        if (nextSibling.isConnected) {
            break;
        }

        nextSibling = chip.nextSibling;
    }
}

function removeChipAndFollowingSpacer(chip) {
    removeChipSpacerAfter(chip);
    chip.remove();
}

function getCurrentSelectionRange() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) {
        return null;
    }

    return selection.getRangeAt(0);
}

function getSelectionRangeWithinElement(element, options) {
    const requireCollapsed = options?.requireCollapsed ?? true;
    const range = getCurrentSelectionRange();
    if (!range) {
        return null;
    }

    if (requireCollapsed && !range.collapsed) {
        return null;
    }

    if (!element.contains(range.startContainer) || !element.contains(range.endContainer)) {
        return null;
    }

    return range;
}

function createCollapsedRange(node, offset) {
    const range = document.createRange();
    range.setStart(node, offset);
    range.collapse(true);
    return range;
}

function getTextCaretContext(range) {
    const container = range.startContainer;
    if (isTextNode(container)) {
        return {
            textNode: container,
            offset: range.startOffset
        };
    }

    if (!isElementNode(container)) {
        return null;
    }

    const childBeforeCaret = range.startOffset > 0
        ? container.childNodes[range.startOffset - 1]
        : null;
    if (isTextNode(childBeforeCaret)) {
        return {
            textNode: childBeforeCaret,
            offset: (childBeforeCaret.textContent || '').length
        };
    }

    const childAtCaret = container.childNodes[range.startOffset] || null;
    if (isTextNode(childAtCaret)) {
        return {
            textNode: childAtCaret,
            offset: 0
        };
    }

    return null;
}

function isMentionBoundaryCharacter(character) {
    return character === zeroWidthSpace || /\s/.test(character);
}

function parseMentionTrigger(textBeforeCaret) {
    const triggerIndex = textBeforeCaret.lastIndexOf('#');
    if (triggerIndex === -1) {
        return null;
    }

    const filter = textBeforeCaret.substring(triggerIndex + 1);
    const startsNewToken = triggerIndex === 0 || isMentionBoundaryCharacter(textBeforeCaret[triggerIndex - 1]);
    if (!startsNewToken || /\s/.test(filter)) {
        return null;
    }

    return {
        filter: filter,
        triggerIndex: triggerIndex
    };
}

function getActiveMentionContext(element) {
    const selectionRange = getSelectionRangeWithinElement(element);
    if (!selectionRange) {
        return null;
    }

    const caretContext = getTextCaretContext(selectionRange);
    if (!caretContext || isNodeInsideFileChip(caretContext.textNode)) {
        return null;
    }

    const textBeforeCaret = (caretContext.textNode.textContent || '').substring(0, caretContext.offset);
    const mentionMatch = parseMentionTrigger(textBeforeCaret);
    if (!mentionMatch) {
        return null;
    }

    return {
        filter: mentionMatch.filter,
        triggerIndex: mentionMatch.triggerIndex,
        range: createCollapsedRange(caretContext.textNode, caretContext.offset)
    };
}

function cacheActiveMentionRange(element, state) {
    const mentionContext = getActiveMentionContext(element);
    if (!mentionContext) {
        clearSavedMentionRange(state);
        return null;
    }

    state.savedMentionRange = mentionContext.range;
    return mentionContext.filter;
}

function getChipInsertionRange(element, state) {
    const savedRange = state.savedMentionRange;
    clearSavedMentionRange(state);

    if (savedRange && element.contains(savedRange.startContainer) && element.contains(savedRange.endContainer)) {
        return savedRange;
    }

    return getSelectionRangeWithinElement(element, { requireCollapsed: false });
}

function removeMentionTriggerTextFromRange(range) {
    const caretContext = getTextCaretContext(range);
    if (!caretContext) {
        return;
    }

    const textBeforeCaret = (caretContext.textNode.textContent || '').substring(0, caretContext.offset);
    const mentionMatch = parseMentionTrigger(textBeforeCaret);
    if (!mentionMatch) {
        return;
    }

    const deleteRange = document.createRange();
    deleteRange.setStart(caretContext.textNode, mentionMatch.triggerIndex);
    deleteRange.setEnd(caretContext.textNode, caretContext.offset);
    deleteRange.deleteContents();

    range.setStart(caretContext.textNode, mentionMatch.triggerIndex);
    range.collapse(true);
}

function moveCaretToTextOffset(textNode, offset) {
    const selection = window.getSelection();
    if (!selection) {
        return;
    }

    const range = document.createRange();
    range.setStart(textNode, offset);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
}

function resizeContentEditable(element) {
    const savedScrollTop = element.scrollTop;
    element.style.height = 'auto';
    element.style.height = `${Math.min(element.scrollHeight, maxContentEditableHeight)}px`;

    // Scroll just enough to keep the caret visible — no more, no less.
    const selectionRange = getSelectionRangeWithinElement(element, { requireCollapsed: false });
    if (selectionRange) {
        const caretRect = selectionRange.getBoundingClientRect();
        if (caretRect.height > 0) {
            const elementRect = element.getBoundingClientRect();
            const caretTop = caretRect.top - elementRect.top + element.scrollTop;
            const caretBottom = caretRect.bottom - elementRect.top + element.scrollTop;

            if (caretBottom > element.scrollTop + element.clientHeight) {
                element.scrollTop = caretBottom - element.clientHeight;
            } else if (caretTop < element.scrollTop) {
                element.scrollTop = caretTop;
            } else {
                element.scrollTop = savedScrollTop;
            }

            return;
        }
    }

    element.scrollTop = savedScrollTop;
}

function insertSoftLineBreak(element) {
    const range = getSelectionRangeWithinElement(element, { requireCollapsed: false });
    if (!range) {
        return;
    }

    range.deleteContents();

    const lineBreak = document.createElement('br');
    range.insertNode(lineBreak);

    // A zero-width text node keeps the caret visible after a trailing <br>.
    const spacer = document.createTextNode(zeroWidthSpace);
    lineBreak.after(spacer);
    moveCaretToTextOffset(spacer, 0);
    resizeContentEditable(element);
}

function getChipBeforeCaret(range) {
    const node = range.startContainer;
    const offset = range.startOffset;

    if (isTextNode(node)) {
        return isFileChipNode(node.previousSibling) ? node.previousSibling : null;
    }

    if (isElementNode(node) && offset > 0) {
        const childBeforeCaret = node.childNodes[offset - 1] || null;
        return isFileChipNode(childBeforeCaret) ? childBeforeCaret : null;
    }

    return null;
}

function removeChipWithBackspace(element, range) {
    const node = range.startContainer;
    const offset = range.startOffset;
    const chip = getChipBeforeCaret(range);
    if (!chip) {
        return false;
    }

    if (isTextNode(node)) {
        const leadingCaretAnchorLength = getLeadingCaretAnchorLength(node.textContent || '');
        if (offset > 0 && offset > leadingCaretAnchorLength) {
            return false;
        }
    }

    removeChipAndFollowingSpacer(chip);
    if (isTextNode(node) && node.isConnected) {
        moveCaretToTextOffset(node, 0);
    }

    resizeContentEditable(element);
    return true;
}

function getChipAfterCaret(range) {
    const node = range.startContainer;
    const offset = range.startOffset;

    if (isTextNode(node)) {
        return offset === (node.textContent || '').length && isFileChipNode(node.nextSibling)
            ? node.nextSibling
            : null;
    }

    if (isElementNode(node)) {
        const childAtCaret = node.childNodes[offset] || null;
        return isFileChipNode(childAtCaret) ? childAtCaret : null;
    }

    return null;
}

function removeChipWithDelete(element, range) {
    const chip = getChipAfterCaret(range);
    if (!chip) {
        return false;
    }

    removeChipAndFollowingSpacer(chip);
    resizeContentEditable(element);
    return true;
}

function handleChipDeletionKey(element, event) {
    if (event.key !== 'Backspace' && event.key !== 'Delete') {
        return false;
    }

    const range = getSelectionRangeWithinElement(element);
    if (!range) {
        return false;
    }

    return event.key === 'Backspace'
        ? removeChipWithBackspace(element, range)
        : removeChipWithDelete(element, range);
}

function shouldSuppressMentionPickerKey(element, state, key) {
    if (!mentionNavigationKeys.has(key)) {
        return false;
    }

    return cacheActiveMentionRange(element, state) !== null;
}

function handleEnterKey(element, state, event) {
    event.preventDefault();

    if (!event.shiftKey && state.enterToSend) {
        return;
    }

    insertSoftLineBreak(element);
}

function handleContentEditableKeydown(element, state, event) {
    if (shouldSuppressMentionPickerKey(element, state, event.key)) {
        event.preventDefault();
        return;
    }

    if (event.key === 'Enter') {
        handleEnterKey(element, state, event);
        return;
    }

    if (handleChipDeletionKey(element, event)) {
        event.preventDefault();
    }
}

function stripCaretAnchors(text) {
    // Enter insertion, chip placement, and browser caret handling all use zero-width spaces.
    // They must not leak into the plain-text model because Markdig treats them as real chars.
    return text.replace(/\u200B/g, '');
}

function appendSerializedNode(node, siblingIndex, parts) {
    if (isTextNode(node)) {
        parts.push(stripCaretAnchors(node.textContent || ''));
        return;
    }

    if (!isElementNode(node)) {
        return;
    }

    if (isFileChipNode(node)) {
        parts.push(`#file:${JSON.stringify(node.dataset.filePath || '')}`);
        return;
    }

    const tagName = node.tagName ? node.tagName.toUpperCase() : '';
    if (tagName === 'BR') {
        parts.push('\n');
        return;
    }

    if (blockContainerTags.has(tagName) && siblingIndex > 0) {
        parts.push('\n');
    }

    appendSerializedChildren(node, parts);
}

function appendSerializedChildren(parent, parts) {
    let childIndex = 0;

    for (const child of parent.childNodes) {
        appendSerializedNode(child, childIndex, parts);
        childIndex++;
    }
}

function finalizePlainText(text) {
    return text
        .replace(/^\u200B+/, '')
        .replace(/\u200B+$/, '')
        .replace(/\s+$/, '');
}

function tryParseFileChipPath(serializedPath) {
    try {
        const filePath = JSON.parse(serializedPath);
        return typeof filePath === 'string' ? filePath : null;
    } catch {
        return null;
    }
}

function consumeSerializedChipSpacer(text, nextIndex) {
    return text[nextIndex] === ' '
        ? nextIndex + 1
        : nextIndex;
}

function buildContentFragmentFromPlainText(text) {
    const fragment = document.createDocumentFragment();
    const chips = [];
    let lastIndex = 0;
    let match;

    fileChipTokenPattern.lastIndex = 0;

    while ((match = fileChipTokenPattern.exec(text)) !== null) {
        const filePath = tryParseFileChipPath(match[1]);
        if (filePath === null) {
            continue;
        }

        appendTextNodeIfNotEmpty(fragment, text.substring(lastIndex, match.index));

        const chipId = generateChipId();
        fragment.appendChild(createFileChipElement(chipId, filePath, getFileNameFromPath(filePath)));
        fragment.appendChild(createChipSpacerNode());
        chips.push({
            chipId: chipId,
            filePath: filePath
        });

        const tokenEndIndex = match.index + match[0].length;
        lastIndex = consumeSerializedChipSpacer(text, tokenEndIndex);
    }

    appendTextNodeIfNotEmpty(fragment, text.substring(lastIndex));
    return {
        fragment: fragment,
        chips: chips
    };
}

function normalizeBrowserPlaceholderMarkup(element) {
    if (element.childNodes.length === 1 && element.firstChild?.nodeName === 'BR') {
        element.replaceChildren();
    }
}

function collectRemovedFileChips(removedNode, chips) {
    if (isFileChipNode(removedNode)) {
        chips.push(removedNode);
        return;
    }

    if (!isElementNode(removedNode)) {
        return;
    }

    chips.push(...removedNode.querySelectorAll(chipSelector));
}

function notifyRemovedFileChips(state, removedNode) {
    const removedChips = [];
    collectRemovedFileChips(removedNode, removedChips);

    for (const chip of removedChips) {
        const chipId = chip.dataset.chipId;
        if (chipId) {
            invokeDotNet(state, 'OnChipRemoved', chipId);
        }
    }
}

function createChipRemovalObserver(state) {
    return new MutationObserver(function (mutations) {
        if (isChipRemovalNotificationSuppressed(state)) {
            return;
        }

        for (const mutation of mutations) {
            for (const removedNode of mutation.removedNodes) {
                notifyRemovedFileChips(state, removedNode);
            }
        }
    });
}

window.cockpit.setupContentEditable = function (id, dotnetRef) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    const state = getContentEditableState(element);
    disconnectTrackedObserver(state);
    removeTrackedEventListener(element, state, 'input');
    removeTrackedEventListener(element, state, 'click');

    state.dotnetRef = dotnetRef;
    state.chipRemovalNotificationDepth = 0;
    clearSavedMentionRange(state);

    addTrackedEventListener(element, state, 'click', function (event) {
        const deleteButton = typeof event.target?.closest === 'function'
            ? event.target.closest(chipDeleteSelector)
            : null;
        if (!deleteButton) {
            return;
        }

        const chip = deleteButton.closest(chipSelector);
        if (!chip) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        removeChipAndFollowingSpacer(chip);
        resizeContentEditable(element);
    });

    addTrackedEventListener(element, state, 'input', function () {
        normalizeBrowserPlaceholderMarkup(element);
        invokeDotNet(state, 'OnContentInput');
    });

    state.observer = createChipRemovalObserver(state);
    state.observer.observe(element, { childList: true, subtree: true });
};

window.cockpit.getActiveMentionFilter = function (id) {
    const element = getContentEditableElement(id);
    if (!element) {
        return null;
    }

    const state = getContentEditableState(element);
    return cacheActiveMentionRange(element, state);
};

window.cockpit.insertFileChip = function (id, chipId, filePath, fileName) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    const state = getContentEditableState(element);
    const range = getChipInsertionRange(element, state);
    if (!range) {
        return;
    }

    removeMentionTriggerTextFromRange(range);

    const chip = createFileChipElement(chipId, filePath, fileName);
    range.insertNode(chip);

    const spacer = createChipSpacerNode();
    chip.after(spacer);
    moveCaretToTextOffset(spacer, chipSpacerText.length);
    resizeContentEditable(element);
};

window.cockpit.removeFileChip = function (id, chipId) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    const chip = findChipById(element, chipId);
    if (!chip) {
        return;
    }

    withSuppressedChipRemovalNotifications(element, function () {
        removeChipAndFollowingSpacer(chip);
    });

    resizeContentEditable(element);
};

window.cockpit.getPlainText = function (id) {
    const element = getContentEditableElement(id);
    if (!element) {
        return '';
    }

    const parts = [];
    appendSerializedChildren(element, parts);
    return finalizePlainText(parts.join(''));
};

window.cockpit.setPlainText = function (id, text) {
    const element = getContentEditableElement(id);
    if (!element) {
        return [];
    }

    const state = getContentEditableState(element);
    const normalizedText = text ?? '';
    clearSavedMentionRange(state);

    return withSuppressedChipRemovalNotifications(element, function () {
        const content = buildContentFragmentFromPlainText(normalizedText);
        element.replaceChildren(content.fragment);
        resizeContentEditable(element);
        return content.chips;
    });
};

window.cockpit.getChipIds = function (id) {
    const element = getContentEditableElement(id);
    if (!element) {
        return [];
    }

    return Array.from(element.querySelectorAll(chipSelector), function (chip) {
        return {
            chipId: chip.dataset.chipId,
            filePath: chip.dataset.filePath
        };
    });
};

window.cockpit.scrollPickerItemIntoView = function (id) {
    const element = document.getElementById(id);
    if (element) {
        element.scrollIntoView({ block: 'nearest' });
    }
};

window.cockpit.autoResizeContentEditable = function (id) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    resizeContentEditable(element);
};

window.cockpit.setupContentEditableBehavior = function (id, enterToSend) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    const state = getContentEditableState(element);
    state.enterToSend = Boolean(enterToSend);
    removeTrackedEventListener(element, state, 'keydown');

    addTrackedEventListener(element, state, 'keydown', function (event) {
        handleContentEditableKeydown(element, state, event);
    });
};

window.cockpit.cleanupContentEditable = function (id) {
    const element = getContentEditableElement(id);
    if (!element) {
        return;
    }

    const state = contentEditableStates.get(element);
    if (!state) {
        return;
    }

    disconnectTrackedObserver(state);
    removeTrackedEventListener(element, state, 'input');
    removeTrackedEventListener(element, state, 'keydown');
    removeTrackedEventListener(element, state, 'click');
    clearSavedMentionRange(state);

    state.dotnetRef = null;
    state.enterToSend = false;
    state.chipRemovalNotificationDepth = 0;
    contentEditableStates.delete(element);
};
