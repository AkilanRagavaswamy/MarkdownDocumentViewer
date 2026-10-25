# Privacy Policy — Markdown Viewer - Simple

**Last updated: 25 October 2026**

Markdown Viewer - Simple is an offline reader for Markdown files. It has no accounts, no
advertising and no analytics, and it does not collect, transmit or sell any personal data.

## What stays on your device

The app stores a few things in its own per-user folder
(`%LOCALAPPDATA%\Packages\AkilanRagavaswamy.MarkdownViewer-Simple_<id>\`). None of it is sent
anywhere.

- **Settings** (`settings.json`) — theme, accent colour, reading density, column layout and the
  other display options.
- **Recent files and zoom levels** — the full paths of files you have opened, and the zoom level
  you chose per file. Paths can reveal file and folder names, so treat this folder as you would
  any other local data.
- **Crash log** (`crash.log`) — if the app closes unexpectedly it appends the error and stack
  trace to a local text file. It is never uploaded; it exists so you can read it or send it to us
  if you choose to.
- **Rendering cache** — a `WebView2` folder used by the Microsoft Edge WebView2 component that
  draws the document, and, for very large documents, a temporary HTML copy that is deleted when
  the tab closes.

Uninstalling the app removes all of it.

## What leaves your device

Nothing, by default. The app contains no code that contacts a server of ours, because we do not
run one. There are three exceptions, all of which you control:

1. **"Load remote content"** (off by default, in Settings). While it is off, the reading surface
   is locked down by a content security policy that permits images only from the folder of the
   document you opened and from the app's own files. Turning it on additionally allows images
   over HTTPS, so opening a document that references a remote image will contact that server.
2. **Links you click.** External links open in your default browser. From that point the site you
   visit, not this app, decides what it collects.
3. **The WebView2 runtime.** Document rendering uses Microsoft Edge WebView2, a Windows component.
   Microsoft may update it and collect its own diagnostic data, governed by the
   [Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement). We do not
   receive any of it.

## Document contents

Your documents are read from disk and rendered locally. They are never uploaded, indexed or
shared, and the app does not read files you have not opened.

## Children

The app is not directed at children and collects no data from anyone, including children.

## Changes

If this policy changes, the updated version will be published at this address with a new date.

## Contact

Questions about this policy: **\<add a contact email or an issue-tracker link before publishing\>**
