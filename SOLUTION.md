# Design Review

## Overview

I reviewed the code and traced the flow across ingestion, enrichment, and persistence.

At a high level, the ingestion service pushes events to RabbitMQ. The enrichment service consumes those events, calls the mock AI API, and then publishes either a success event or a failure event. The persistence service then stores the final result in Postgres.

## Ingestion

- From what I understood, the ingestion service pushes the initial events to the queue.
- RabbitMQ persistence helps here, so messages should not be lost once they are successfully written.
- The only issue I see at this stage is around writing to RabbitMQ itself.
- If a write fails mid-batch, we might retry the whole batch again, which could create duplicate entries.
- One possible improvement here is to introduce an idempotent key, maybe a message GUID or content hash, and use that consistently across the pipeline.

## Enrichment

- Each event is picked up by the enrichment service, which then calls the mock API to get more details.
- I think the biggest flaw in the original flow was here.
- We get rate limited very quickly, and there was also no proper handling for failure.

## Changes Made

- The queue consumer itself retries the whole message a couple of times at the MassTransit level.
- I added retry logic when the enrichment service gets rate limited.
- The retry uses exponential backoff with jitter to avoid a thundering herd situation where we keep hitting the API again and again too quickly.
- This should improve AI API availability and reduce how often we get rate limited.
- I also added retries for server-side errors so that transient failures get a few chances before we give up.
- So now we have a small number of broker-level retries, plus more targeted retries inside the consumer with exponential backoff.
- The minimum backoff time can still be tuned later based on the actual concurrency we want to run with, but for now this looks reasonable.

## Success Flow

- If the AI call succeeds, the enrichment service publishes the success event.
- The persistence consumer then reads that event and writes the final result to Postgres.

## Failure Flow

- If retries are exhausted, the enrichment service publishes a failure event instead.
- That failure event is consumed by the persistence service and written to Postgres.
- This gives us a durable record of failures so they can be inspected later and retried if needed.

## Persistence Notes

- Right now, persistence is still done one message at a time.
- That is simple and safe, but it also means more round trips to Postgres.
- The current biggest bottleneck still looks like the AI API, not the database, so I think this is acceptable for now.

## Possible Future Improvements

- We could batch writes to reduce database round trips.
- The tradeoff is that if one insert in the batch fails, rollback and retry logic becomes more complicated.
- If we do move in that direction, an upsert strategy with a stable idempotent key would probably be the safest approach.

## Why Exponential Backoff

- For the mocked API, this probably does not matter too much.
- But in a production system, it matters a lot more.
- If a third-party service thinks we are effectively behaving like a DDoS source, it may block us for a much longer period of time.
- Exponential backoff helps us back off more responsibly instead of repeatedly hitting a service that is already overloaded or protecting itself.
