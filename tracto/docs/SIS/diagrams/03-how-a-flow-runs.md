# Page 3: How a flow runs

The same sequence for all five flows, from setting it up to checking the result. Each column (lane) is one actor; read the boxes from top to bottom. A box sits in the lane of whoever does that step.

## The lanes

| Lane | Who |
|---|---|
| Operator | The person responsible for the flows |
| Orchestrator UI | The web console |
| Orchestrator | The service that holds schedules and runs jobs |
| Desktop runtime | The program that runs the main job |
| Go API | The program that does the flow's work |
| Sheet, file, mail, program, work-item, Extraction APIs | The services the Go API calls (page 2) |

## The boxes, in order

| # | Box | Lane | What happens |
|---|---|---|---|
| 1 | **Set up once per flow** | Operator | The flow's main job (for example `bankcheck-main`) is published. It has a single step: an HTTP call to the Go API. Then a schedule is created for it: the time, the time zone (India or Australia) and the default parameters (which entities, which date). This is done once, not every day. |
| 2 | **Schedules page lists upcoming and completed runs** | Orchestrator UI | The console shows when each flow will run next and how the last runs ended. |
| 3 | **Run now, or rerun for one entity or date** | Operator | Instead of waiting for the schedule, the operator can start a flow at once, for example to redo one entity or a past date. For flow 5, a rerun on a day that already succeeded needs a "force" flag, because running twice would create receipts twice. |
| 4 | **Queue a job for the main job** | Orchestrator | At the scheduled time (or on Run now) the Orchestrator puts a job in its queue with the parameters. |
| 5 | **Pick up the job and run its one step** | Desktop runtime | The desktop runtime takes the job and makes the one HTTP call to the Go API, passing the parameters. |
| 6 | **Check the params; create a batch; answer at once** | Go API | The Go API checks the parameters, creates a batch (one run of the flow) with one item per entity, file or document, and answers straight away: **accepted** with the batch id. It answers **busy** if the same flow is still running, or **invalid** if a parameter is wrong. |
| 7 | **Step done: report the result** | Desktop runtime | The main job is finished within seconds. The desktop runtime is free again. Nothing was typed, clicked or opened on the screen. |
| 8 | **Run record** | Orchestrator | The run is recorded as SUCCEEDED with the batch id in its outputs. If the Go API answered busy or invalid, or could not be reached, the run is FAILED with the reason. |
| 9 | **Work through the items in the background** | Go API | Meanwhile the Go API works through the items one by one. Each item moves from Pending to Running to Done, Failed or Held. A failed item can be tried again, and the number of attempts is counted. One failed item never stops the others. |
| 10 | **Each step calls the API it needs** | APIs | Reading a workbook, checking a folder, fetching mail, running a program, claiming a work item, reading a PDF: each is a call to the matching service. The flow pages show which step calls which. |
| 11 | **Send the flow's mails and record the batch result** | Go API | At the end the Go API sends the flow's mails and stores the result: how many items are done, failed or held. |
| 12 | **Go mail API sends** | APIs | The mail goes out through Microsoft Graph, or SMTP for the in-house server. |
| 13 | **Console** | Orchestrator UI | The console shows the run, the batch result and each item's status with its reason. When a run fails, an alert mail goes to the operations recipients. |
| 14 | **Check the outcome; rerun** | Operator | The operator checks the result and, if needed, reruns only the failed items or one entity with Run now. |

## Line labels

| Label | Meaning |
|---|---|
| Run now | The operator starts the job by hand |
| job handed out | The Orchestrator gives the queued job to the desktop runtime |
| HTTP call: start | The main job's single step |
| accepted + batch id | The Go API's immediate answer |
| result | The desktop runtime reports the job's end to the Orchestrator |
| continues | After answering, the Go API carries on with the work |
| calls | The Go API uses a service |
| batch result | The flow's final counts reach the console |

## The table at the bottom

| Column | Meaning |
|---|---|
| Flow | The five flows |
| Replaces UiPath | The UiPath process each one replaces, for reference |
| Main job | The name of the flow's one-step job |
| When | The planned run time. Times marked "proposal" or "to confirm" are not settled yet |
| Entities | Which entities the flow covers |

The Indian collection accounting run is not in the table: a person still starts it from the web page `ca_with_pa`.
