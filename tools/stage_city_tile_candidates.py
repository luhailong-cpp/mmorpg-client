"""Stage verified local city candidates for Unity Editor review, never production Resources."""
from pathlib import Path
import argparse,hashlib,json,shutil,struct
from datetime import datetime,timezone

APPEARANCES=('tianyong_festival','penglai_day','penglai_mid_autumn','donghai_day','donghai_lantern','lanxian_day','lanxian_spring')
ROOT=Path(__file__).resolve().parents[2]/'image/qdao_city_tiles_4k_20260916'
PROJECT=Path(__file__).resolve().parents[1]
DEST=PROJECT/'Assets/Editor/CityTiles4KReview'

def sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for data in iter(lambda:f.read(1024*1024),b''):h.update(data)
 return h.hexdigest()

def inside(path,root):
 path=path.resolve();root=root.resolve()
 if not path.is_relative_to(root):raise ValueError(f'Path outside expected root: {path}')
 return path

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args()
 ledger=ROOT/'builtin_q64_production/current-batch.json';batch=json.loads(ledger.read_text(encoding='utf-8-sig'))
 assert batch['acceptedDeliveryTileCount']==0 and not batch['runtimePublished'],'Review tool expects unaccepted candidates'
 entries=[];seen=set()
 for candidate in batch['candidates']:
  app,tile=candidate['appearance'],candidate['tile']
  assert app in APPEARANCES
  import re
  m=re.fullmatch(r'r(\d{2})_c(\d{2})',tile);assert m,tile
  row,col=map(int,m.groups());assert 1<=row<=16 and 1<=col<=16
  assert (app,tile) not in seen;seen.add((app,tile))
  source=inside(ROOT/candidate['file'],ROOT);source_hash=sha(source)
  assert source_hash==candidate['sha256'],f'Source changed: {source}'
  header=source.read_bytes()[:24]
  assert header[:8]==b'\x89PNG\r\n\x1a\n' and header[12:16]==b'IHDR'
  assert struct.unpack('>II',header[16:24])==(4096,4096)
  assert candidate['finalPixelRectXYWH']==[(col-1)*4096,(row-1)*4096,4096,4096]
  w=candidate['worldRect'];expected={'x':50+(col-1)*18.75,'z':300-row*18.75,'width':18.75,'height':18.75}
  assert all(abs(w[k]-v)<1e-5 for k,v in expected.items()),candidate
  for key in ('qa','assembly'):
   evidence=inside(ROOT/candidate[key],ROOT);assert evidence.is_file()
   assert sha(evidence)==candidate[key+'Sha256'],f'{key} evidence changed'
  # Content-addressed filenames preserve every previously staged revision.
  destination=inside(DEST/'Tiles'/app/(tile+'_'+source_hash[:16]+'.png'),DEST)
  entries.append({'appearance':app,'tile':tile,'row':row,'column':col,'sourcePath':str(source),'sourceSha256':source_hash,'assetPath':destination.relative_to(PROJECT).as_posix(),'x':expected['x'],'z':expected['z'],'size':18.75,'qaPath':str(ROOT/candidate['qa']),'qaSha256':candidate['qaSha256'],'assemblyPath':str(ROOT/candidate['assembly']),'assemblySha256':candidate['assemblySha256']})
 catalog={'schemaVersion':1,'purpose':'local_candidate_editor_review_only','tilePixels':4096,'wholeCityPixels':65536,'rows':16,'columns':16,'acceptedDeliveryTileCount':0,'runtimePublished':False,'sourceLedgerPath':str(ledger),'sourceLedgerSha256':sha(ledger),'createdAtUtc':datetime.now(timezone.utc).isoformat(),'appearances':list(APPEARANCES),'candidates':entries}
 for e in entries:
  target=PROJECT/e['assetPath']
  if args.check:assert target.is_file() and sha(target)==e['sourceSha256'],target
  elif target.exists():assert sha(target)==e['sourceSha256'],target
  else:target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(e['sourcePath'],target);assert sha(target)==e['sourceSha256']
 catalog_file=DEST/'catalog.json'
 if args.check:
  saved=json.loads(catalog_file.read_text(encoding='utf8'));assert saved['sourceLedgerSha256']==catalog['sourceLedgerSha256'];assert saved['candidates']==entries
 else:
  DEST.mkdir(parents=True,exist_ok=True)
  if catalog_file.exists():
   history=DEST/'CatalogHistory'/f'{sha(catalog_file)[:16]}.json';history.parent.mkdir(exist_ok=True)
   if not history.exists():shutil.copyfile(catalog_file,history)
  tmp=catalog_file.with_suffix('.json.writing');tmp.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf8');tmp.replace(catalog_file)
 print(json.dumps({'mode':'check' if args.check else 'stage','candidates':len(entries),'appearanceCounts':{a:sum(e['appearance']==a for e in entries) for a in APPEARANCES},'destination':str(DEST),'productionManifestWritten':False},ensure_ascii=False))

if __name__=='__main__':main()
