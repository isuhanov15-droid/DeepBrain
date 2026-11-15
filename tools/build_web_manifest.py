"""Regenerate hashes for installable Web source files, without runtime data."""
from pathlib import Path
import ast,hashlib,json,sys
base=Path(sys.argv[1] if len(sys.argv)>1 else 'web')
tree=ast.parse((base/'install.py').read_text(encoding='utf-8'))
files=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='files' for t in n.targets))
values={name:hashlib.sha256((base/name).read_bytes()).hexdigest() for name in files}
(base/'manifest.json').write_text(json.dumps(values,indent=2)+'\n',encoding='utf-8')
print('Updated',len(values),'source checksums')
