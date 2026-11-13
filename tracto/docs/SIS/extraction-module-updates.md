# Extraction module: updates needed

What the existing extraction module still has to gain to read SIS payment advices. The module team makes these updates; Tracto relies on them.

## Current position

- **PDF extraction** is in the module, but some features are missing.
- **Image extraction** is not in the module.

## Updates the module needs

1. **PDF password management.** The module has none. Password-protected PDFs have to be unlocked before they are extracted.
2. **Image extraction.** Images such as JPEG and JPG cannot be extracted today.
3. **Any other format that needs LLM extraction.** It goes through the same module, so it needs the same update.

## Handled in Tracto

- **Excel advices.** Tracto validates, cleans and maps them and writes the advice workbook, so the module is not involved.
- The rest of the PDF and image flow around the extraction: validation, customer rules, the advice workbook, the exceptions record and the notification mails.

## How the proposal treats it

The proposal presents extraction as functionality built in this project. In practice the PDF and image advice flow depends on the module updates above, so the estimate assumes they arrive.

## To confirm with the module team

- When the updates will be available, since the PDF and image advice flow depends on them.
