SELECT countMerge(events) as Count
FROM billing_usage_v1
WHERE app_id IN ({app_ids})
AND year = {year}
AND month = {month}
