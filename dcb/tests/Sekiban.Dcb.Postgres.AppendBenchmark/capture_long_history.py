"""EXPLAIN production bootstrap/reconciliation on two keys with 10k rows each.
Requires the disposable g162-plans container (see plans/README.md).
"""
import pathlib, re, sys
# Reuse only schema, SQL extraction and psql helpers; do not run the old matrix.
helper = pathlib.Path(__file__).with_name('plan_capture.py').read_text().split('for n,writes,writers in ')[0]
sys.argv = ['capture_long_history.py', 'rev5']
exec(helper)
psql(schema)
psql('''INSERT INTO dcb_tags ("ServiceId","Tag","TagGroup","EventType","SortableUniqueId","EventId","CreatedAt")
SELECT 'benchmark',tag,'history','Marker',lpad(i::text,20,'0'),'00000000-0000-0000-0000-000000000001',now()
FROM generate_series(1,10000) i CROSS JOIN (VALUES ('long:a'),('long:z')) k(tag);
INSERT INTO dcb_tag_heads VALUES ('benchmark','long:a',NULL),('benchmark','long:z',NULL);''')
queries = [q for q in statements(source) if 'CROSS JOIN LATERAL' in q]
assert len(queries) == 2
for stats in ['fresh', 'analyzed']:
 if stats == 'analyzed': psql('ANALYZE;')
 for mode in ['custom', 'generic']:
  sql = f"\\pset pager off\nSET plan_cache_mode=force_{mode}_plan;\n"
  for i,q in enumerate(queries):
   names=list(dict.fromkeys(re.findall(r'@(\w+)',q)))
   prepared=re.sub(r'@(\w+)',lambda m:'$'+str(names.index(m[1])+1),q)
   vals={'serviceIds':"ARRAY['benchmark','benchmark']::text[]",'tags':"ARRAY['long:a','long:z']::text[]",'positions':"ARRAY['00000000000000000001','00000000000000000001']::text[]"}
   sql+=f"\\echo {'BOOTSTRAP' if i==0 else 'RECONCILIATION'}: {q}\nBEGIN; PREPARE long{i} ({','.join('text[]' for _ in names)}) AS {prepared};\nEXPLAIN (ANALYZE, BUFFERS) EXECUTE long{i}({','.join(vals[k] for k in names)});\nDEALLOCATE long{i}; ROLLBACK;\n"
  (out/f'rev5-long-history-{stats}-{mode}.txt').write_text(psql(sql))
print('Captured 10,000 rows per requested tag, both set-based statements, fresh/analyzed custom/generic.')
