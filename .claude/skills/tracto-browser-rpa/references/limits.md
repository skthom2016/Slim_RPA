# What the browser runtime cannot do

Tell the developer plainly when one of these blocks the request. Do not invent an action.

## No branching, no loops, no retry

Steps run in order and the first failure stops the run. There is no `if`, no loop, no retry and
no error handler. To do something to five rows, write five sets of steps, or use `getTexts` to
read them all in one go and hand the joined text on.

## Only flat text comes back

A read publishes a single `value`. A table cannot be read into rows and columns; `getTexts`
joins the matches into one string with a separator, and anything beyond that has to be split by
whatever consumes it. There is no JSON path, no regular expression and no arithmetic.

## No CAPTCHA, and no bypassing one

Google answers automated Chromium with a CAPTCHA page (`/sorry/`). The runtime does not solve
or bypass CAPTCHAs and never will. Bing works for a search. If a site puts a CAPTCHA in the
path, that path cannot be automated.

## Your own Chrome cannot be driven

Since Chrome 136, Chrome ignores the debugging port on its normal profile, so the browser you
use day to day is off limits. A Tracto profile is always a separate browser folder owned by the
worker. This is a deliberate protection in Chrome, not a gap to work around.

## One run at a time per profile

A browser locks its profile folder while open. A second run naming the same profile fails until
the first finishes. Segments that each need the same signed-in account therefore queue behind
one another; segments needing no sign-in can run in parallel.

## Sign-ins expire and cannot be renewed by a script

A profile keeps cookies, not credentials. When the site ends the session or asks for MFA, the
run lands on a login page and fails. Someone has to run `--login` again. Plan for it on any
schedule that runs unattended for long.

## No file downloads or uploads from a step

There is no action that downloads a file to a path or attaches one to a form. If a flow needs a
file, a desktop segment has to handle it.

## No reading a page's structure

There is no action that returns the DOM, a list of elements or a page's HTML. You cannot
discover a selector from inside a script; you have to know it when you write the script. Use the
Designer's recorder, or look at the page yourself.

## CSS may be switched off

The host has an `AllowCss` policy. Where it is off, a `css` or `xpath` selector is rejected at
validation and only the semantic ones work. Prefer `role`, `label` and `placeholder` so a script
survives that setting.

## A stale selector fails late

Nothing checks a selector until the step runs. A page redesign turns every affected step into a
`TARGET_NOT_FOUND` at run time. Favour `role` and `label`, which survive a redesign far better
than a generated CSS id.
