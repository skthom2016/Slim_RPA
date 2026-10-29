# Tracto — Designer Recording Cleanup Guide

When the Designer records a session it captures everything — including noise that must be removed before the script will run correctly. Follow these steps in order every time you are given a raw recording file.

---

## Step 1 — Delete Noise Steps

Remove any step that matches one of these patterns:

| Pattern to delete | Why it's noise |
|-------------------|---------------|
| `focus` with empty `parameters: {}` | Unfocused event — no target was captured |
| `focus` with `window: ""` or `window` key missing | Designer failed to identify the window |
| `navigate` with empty `parameters: {}` | Spurious window-activation event |
| `click` with only `coordinates`, no `window`, in the first 5 steps | User clicking into the Designer UI itself before automation started |
| Any step whose `description` contains `"Tracto Designer"` | Recorder captured clicks on the Designer app itself |
| Any step with `window: "DesktopWindowXamlSource"` | Windows internal XAML host — not a real automation target |
| Any step with `window: "CoreInput"` | Windows internal input host — not a real automation target |

---

## Step 2 — Convert VK Codes to a `type` Action

The recorder writes individual keystrokes as `key` actions with raw Windows virtual key codes (`VK_48`, `VK_45`, etc.). Replace any consecutive group of `key` steps that spell out a word or phrase with a single `type` action.

### VK Code → Character Map

```
Letters:
VK_41=A  VK_42=B  VK_43=C  VK_44=D  VK_45=E  VK_46=F  VK_47=G
VK_48=H  VK_49=I  VK_4A=J  VK_4B=K  VK_4C=L  VK_4D=M  VK_4E=N
VK_4F=O  VK_50=P  VK_51=Q  VK_52=R  VK_53=S  VK_54=T  VK_55=U
VK_56=V  VK_57=W  VK_58=X  VK_59=Y  VK_5A=Z

Digits:
VK_30=0  VK_31=1  VK_32=2  VK_33=3  VK_34=4
VK_35=5  VK_36=6  VK_37=7  VK_38=8  VK_39=9

Special:
VK_20=Space  VK_0D=Enter  VK_08=Backspace  VK_09=Tab
VK_BC=,  VK_BE=.  VK_BF=/  VK_BA=;  VK_BB==  VK_BD=-
```

### Example

Recorder output:
```json
{ "action": "key", "parameters": { "key": "VK_48" } },
{ "action": "key", "parameters": { "key": "VK_45" } },
{ "action": "key", "parameters": { "key": "VK_4C" } },
{ "action": "key", "parameters": { "key": "VK_4C" } },
{ "action": "key", "parameters": { "key": "VK_4F" } },
{ "action": "key", "parameters": { "key": "VK_20" } }
```

Clean version:
```json
{ "action": "type", "parameters": { "value": "HELLO " } }
```

**Do not convert VK codes that represent control keys** (`VK_0D`=Enter, `VK_1B`=Escape, etc.) — leave those as `key` actions with the appropriate `{ENTER}`, `{ESC}` notation.

---

## Step 3 — Add Delays Between Major Actions

The recorder captures no timing. Add `delay` steps wherever the UI needs time to respond:

| Transition | Recommended delay |
|-----------|------------------|
| After `{LWIN}s` (Windows Search opens) | 800 ms |
| After typing into a search box | 1000 ms |
| After pressing Enter to open an app | 2000 ms |
| After `app.launch` | 2000 ms |
| After clicking a button that opens a dialog | 500 ms |
| After `file.saveAs` | 1000 ms |
| After any navigation / window switch | 500 ms |

Delay format — remember the value must be a string:
```json
{ "stepNumber": N, "description": "Wait for app to open", "action": "delay", "parameters": { "ms": "2000" } }
```

---

## Step 4 — Give the Script a Clean Identity

Replace the recorder's auto-generated id and name with meaningful values:

```json
{
  "id": "descriptive-kebab-case-id",
  "name": "Human Readable Name",
  "version": "1.0.0",
  "targetRuntime": "desktop"
}
```

- `id`: lowercase, hyphens only, no spaces (e.g. `open-notepad-and-save`)
- `name`: human-readable title (e.g. `Open Notepad and Save`)
- `version`: always start at `"1.0.0"` for a fresh script

---

## Step 5 — Add `file.saveAs` if the Recording Ends Without Saving

Raw recordings often end abruptly when the user stopped recording. If the intent included saving a file, add this step:

```json
{
  "stepNumber": N,
  "description": "Save the file",
  "action": "file.saveAs",
  "parameters": {
    "filePath": "%USERPROFILE%\\Downloads\\output.txt",
    "windowTitle": "Notepad",
    "overwrite": "true"
  }
}
```

---

## Step 6 — Renumber Steps

After all deletions and insertions, renumber `stepNumber` sequentially starting from 1. No gaps.

---

## Step 7 — Validate

- Every parameter value must be a **string** (not a number or boolean)
- The `id` field must be present
- Steps must have `stepNumber`, `action`, and `parameters`
- Validate the final JSON against `tracto/script.schema.json`
