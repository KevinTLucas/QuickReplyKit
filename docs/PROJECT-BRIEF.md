# QuickReplyKit — Original Project Brief (source of truth)

> This is the original instruction/"gauntlet" prompt that kicked off the project, preserved verbatim
> so requirements are never lost. `ARCHITECTURE.md` is the design that implements it; `ROADMAP.md`
> tracks delivery against it.

---

Build a Windows desktop application called QuickReplyKit.
QuickReplyKit is a lightweight response launcher for support teams. Its purpose is to let a user quickly find a prewritten response and insert it into whatever application they are currently working in.
The application should feel extremely fast and lightweight. It should open from a global keyboard shortcut, appear as a small utility window, and disappear again after the user selects a response.
Use the installed /loop skill to work through the project iteratively. Break the work into logical phases, validate each phase before moving on, and use subagents for implementation, QA, testing, UI review, and regression checks.
Use the installed /ponytail skill for testing, QA, UI validation, and final verification where appropriate.

## Core User Experience
The normal workflow should be:
1. The user is working in another application, such as a browser, CRM, Outlook, Teams, or another Windows application.
2. The user presses a configurable global keyboard shortcut.
3. QuickReplyKit opens immediately.
4. The user searches or browses for a prepared response.
5. The user selects a response and may preview it before inserting it.
6. QuickReplyKit closes or hides.
7. Focus returns to the application the user was previously using.
8. The response is inserted at the user's existing cursor position.
9. The user's existing clipboard content should remain usable after the response is inserted.
10. QuickReplyKit responses should not clutter the user's Windows clipboard history.

## Response Library
QuickReplyKit should maintain a response library organized by folders and subfolders.
The folder structure should directly determine how responses are organized and displayed inside the application.
For example:
Clever Apps → IXL → Licensing → No Campus License
Users should be able to share response folders with each other by copying them into their own QuickReplyKit response library.
If someone sends another user an entire IXL response folder, the receiving user should be able to add that folder and immediately see the same hierarchy and responses.
The application should automatically recognize new or changed response files and folders without requiring the application to be rebuilt.

## Main Launcher
The main QuickReplyKit window should be compact, simple, and optimized for speed. It should include:
Search · Folder/category navigation · Response results · Response preview · Paste action · Plain-text paste action · Copy action
The launcher should be fully resizable and adapt cleanly to different window sizes. The default size should remain relatively small because the application is primarily opened over another application using a keyboard shortcut. The UI should be elegant but minimal. Avoid unnecessary clutter. Keyboard navigation should be a major part of the experience.
A user should be able to do something like: Hotkey → type a few characters → arrow to the desired response → press Enter → response inserted.
Single-click should select and preview a response. Do not automatically insert a response simply because the user single-clicked it.
The application must not interfere with or replace right-click menus in browsers, CRMs, or other applications.

## Search
Users should be able to search their entire response library quickly, by: response name, application name, folder/category, tags or related keywords. Search results should update quickly while the user types.

## Response Preview
Users should be able to preview a response before inserting it. The preview should preserve the intended formatting and resize cleanly with the application window.

## Rich Text
Responses should support normal rich-text formatting used in support communications: bold, italic, underline, bullet lists, numbered lists, hyperlinks, paragraphs and line breaks.
When a response is inserted into an application that supports rich text, formatting should be preserved. There should also be an option to insert the same response as plain text.

## Create Responses
Users should be able to create new responses directly from QuickReplyKit. When creating a response, users should be able to choose where it belongs in the response hierarchy. If the desired folder or category does not already exist, QuickReplyKit should allow the user to create the necessary folder structure as part of creating the response. Users should not need to manually create folders outside the application unless they want to.

## Response Editor
QuickReplyKit should include a built-in rich-text editor. Users should be able to edit responses visually without needing to understand or edit markup or response files manually. The editor should remain simple and focused on support responses rather than becoming a full word-processing application.

## Response Management
Users should be able to: Create · Edit · Duplicate · Rename · Move to another folder · Delete a response · Create a response from clipboard content · Open the response's folder in Windows Explorer · Open the main response library folder in Windows Explorer.
Destructive actions such as deleting a response should require confirmation.

## Create From Clipboard
Users should be able to copy text they have already written elsewhere and quickly turn that clipboard content into a new QuickReplyKit response. This should open the response creation/editor experience with the clipboard content already populated.

## Import and Export
QuickReplyKit should support: Exporting a single response · an entire response folder · the full response library · Importing a single response · a response folder · a larger response library.
Users should also be able to share responses simply by copying folders or response files directly into another user's QuickReplyKit response library. When imported content conflicts with an existing response, QuickReplyKit should provide a clear way to decide what to keep. Invalid imported content should not prevent valid responses from being imported.

## Favorites
Users should be able to mark responses as favorites. Favorites should provide a fast way to access frequently used responses.

## Recent Responses
QuickReplyKit should maintain a list of recently used responses so frequently repeated work can be done quickly.

## Settings
QuickReplyKit should provide basic settings for: Global keyboard shortcut · Window size and position · Default response behavior where appropriate. User preferences should persist between application restarts.

## System Tray
QuickReplyKit should be able to remain available in the Windows system tray. The tray menu should provide: Open QuickReplyKit · Open Response Library · Settings · Exit.

## Window Behavior
QuickReplyKit should remember a useful window size and position. If the user changes monitors or monitor layouts, the application should still reopen somewhere visible and usable.
The launcher should handle: compact window sizes · larger window sizes · Windows display scaling · long response names · deep folder structures · empty folders · empty search results · invalid response files · large response libraries.
Controls should not overlap or become unusable as the window is resized.

## Performance Expectations
QuickReplyKit should feel immediate. The application should remain lightweight while idle. Opening the launcher from the keyboard shortcut should feel instant. Searching through the response library should remain fast even with a large number of responses. The application should not feel like a large desktop suite or web application.

## Scope
QuickReplyKit is a local response launcher and response-library manager. For this MVP, do NOT add: CRM-specific integrations · Browser extensions · Page scraping · Dynamic placeholders · Dynamic values pulled from webpages · Ticket automation · CRM APIs · AI-generated responses.
Keep the MVP focused on making prepared responses extremely fast to find, manage, share, and insert.

## Validation
Work through the MVP incrementally. After each major feature or phase: test it · validate the UI · validate resizing and keyboard behavior · test for regressions · fix failures before moving forward.
Use separate subagents for implementation and independent validation. Use ponytail for QA, testing, UI validation, and final verification.
The MVP should not be considered complete until the full workflow has been validated:
User is working in another application → presses the QuickReplyKit hotkey → finds a response → previews/selects it → inserts it → returns immediately to their work.
The final application should be a polished, lightweight, portable Windows utility that can be distributed easily to other users.
