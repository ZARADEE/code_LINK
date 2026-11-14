from pathlib import Path
from PIL import Image, ImageOps
import shutil
root=Path(__file__).resolve().parent.parent
out=root/'assets';out.mkdir(exist_ok=True)
icon=ImageOps.fit(Image.open(root/'naiwa_0066.jpg').convert('RGB'),(512,512))
icon.save(out/'icon.png')
icon.save(out/'app.ico',sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
shutil.copyfile(root/'naiwa_0050.gif',out/'startup.gif')
android=root/'android/app/src/main'
(android/'res/drawable').mkdir(parents=True,exist_ok=True)
(android/'assets').mkdir(parents=True,exist_ok=True)
icon.save(android/'res/drawable/app_icon.png')
shutil.copyfile(out/'startup.gif',android/'assets/startup.gif')
