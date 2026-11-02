# What the runtime cannot do (and what to do about it)

Tell the developer plainly when a request hits one of these. Do not invent an action name; an unknown action fails the whole run with `UnknownAction:<name>`.

## Constructs that do not exist
| Missing | Workaround |
|---|---|
| Loops | Repeat steps in the JSON, or a set-based sheet-service call (`/apply`, `/update`) |
| Conditions / branches | None. A script is a straight line. Split into separate scripts that a person or scheduler chooses between. |
| Error handling, retry, timeout per step | None. The first failure stops the run. Add `delay`s and specific window titles to avoid failures. |
| Setting or computing a variable | Only `storeAs` outputs and `${now.*}` exist. No string functions, no arithmetic. |
| Sub-flows, includes, parameters passed to a script | None. Copy steps. |
| Script inputs per run | None in the script format. Values are written into the script. |

## Actions that do not exist
| Need | Today's workaround | Ask the core team for |
|---|---|---|
| Create / copy / move / delete / rename files and folders | Run dialog + `cmd /c` (recipe 4) or Explorer keystrokes (recipe 5). No result or error is visible to the script. | `file.copy`, `file.move`, `file.delete`, `file.mkdir`, `file.exists` that return success and fail on error |
| Read a file or its size into a variable | `file.saveAs` returns `fileSizeBytes` for files it saved. Nothing else. | `file.read`, `file.exists` |
| Write a file with given content | Notepad: `notepad.type` + `file.saveAs` (recipe 3) | `file.write` |
| Read the clipboard, or set it directly | `notepad.type` sets it as a side effect; copying uses keystrokes | `clipboard.get`, `clipboard.set` |
| Read text from a PDF | Keyboard copy from a viewer; did not work on the test machine (recipe 9) | `pdf.extractText` (with optional page and region) |
| Read text from the screen or a control | None. The script cannot see the screen. | UI Automation text read, OCR |
| Wait until a window or file appears | Fixed `delay` | `window.wait`, `file.waitFor` |
| Check whether a window exists, or close one by handle | `key Alt+F4` with a window title | `window.exists`, `window.close` |
| Run a command and capture its output/exit code | Run dialog; output lost | `shell.run` returning `stdout`, `exitCode` |
| Double-click, drag, hold, mouse move | Only single left/right click | `click` variants |
| Type special keys inside text | Separate `key` steps | richer `type` |
| Excel / Word / Outlook / PowerPoint automation | Sheets via sheet-service; others by keystrokes only | Office actions, email read/send/attachments |
| Dialog/popup handling (appears sometimes) | Fixed handling only if the dialog always appears; use `key` with the dialog's title | conditional steps or `dialog.dismiss` |
| Browser automation, terminal sessions | Out of scope for this skill (separate runtimes) | |

## Behaviour to design around
- **Success is not proof.** See SKILL.md rule 2. Key and click steps report success when sent.
- **One desktop, one keyboard.** A person using the machine, a notification, a lock screen or a different foreground window breaks keystroke steps. The machine must be unlocked, awake and idle.
- **Window titles change.** Win 11 Notepad shows the active tab only. Browsers show the page title. A title that matched yesterday may not match today.
- **Pixel clicks are fragile.** Coordinates and offsets depend on screen size, scale, theme, window size and app version. Keyboard shortcuts survive changes better.
- **Foreground rules.** Windows may refuse to bring a window forward in some situations. If a `navigate` seems ignored, the next keystroke goes elsewhere.
- **Per-machine differences.** Default apps, installed software, browser policies and Explorer's delete-confirmation setting differ between machines. Examples seen: no `.pdf` handler installed; browsers that would not put PDF text on the clipboard; Delete confirmation on. Write scripts that survive these, and state assumptions in the hand-over.
- **Arguments with spaces** in `app.launch` `args` cannot be passed (split on spaces). Use the Run dialog with quotes.
- **No scheduled inputs or secrets store** in the script: never put passwords in a script.

## Adding something to the runtime (for the core team)
A new action is a class implementing `IActionHandler` in `tracto/desktop-runtime/src/Tracto.Desktop.Actions/Handlers/`, registered in `tracto/desktop-runtime/src/Tracto.Desktop.Host/Program.cs`. After adding one, update `scripts/actions.json` and `references/actions.md` in this skill; the validator warns when a handler exists that it does not know.
