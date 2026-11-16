from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN

DOCS = Path(__file__).parent
PPT = DOCS / 'TileKeeper-architecture.pptx'
ASSETS = DOCS / 'illustrations'
ASSETS.mkdir(exist_ok=True)

W, H = 1200, 675
NAVY = '#142337'; BLUE = '#0057B8'; AMBER = '#E68A00'; GREEN = '#1F7A4C'; BG = '#F3F5F8'; WHITE = '#FFFFFF'; TEXT = '#1B2733'; MUTED = '#4B5A6B'; LINE = '#D5DCE6'

def font(size, bold=False):
    candidates = ['C:/Windows/Fonts/arialbd.ttf' if bold else 'C:/Windows/Fonts/arial.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf' if bold else '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf']
    for p in candidates:
        if Path(p).exists(): return ImageFont.truetype(p, size)
    return ImageFont.load_default()

def text(draw, xy, value, size=28, fill=TEXT, bold=False, anchor=None):
    draw.text(xy, value, font=font(size, bold), fill=fill, anchor=anchor)

def chrome(draw, title):
    draw.rectangle((0,0,W,H), fill=BG)
    draw.rectangle((0,0,W,62), fill='#E9EDF3')
    text(draw,(26,31),title,25,NAVY,True,'lm')
    for x,c in [(1100,'#D9534F'),(1135,'#F0AD4E'),(1170,'#5CB85C')]: draw.ellipse((x-9,22,x+9,40),fill=c)

def save(name, painter):
    im=Image.new('RGB',(W,H),BG); d=ImageDraw.Draw(im); painter(d); path=ASSETS/name; im.save(path); return path

def programs(d):
    chrome(d,'TileKeeper  |  Programs')
    text(d,(38,104),'Programs',34,NAVY,True)
    d.rectangle((38,155,1162,548),fill=WHITE,outline=LINE,width=3)
    d.rectangle((38,155,1162,215),fill='#E9EDF3')
    text(d,(70,185),'Program',23,MUTED,True,'lm'); text(d,(520,185),'File / shortcut',23,MUTED,True,'lm')
    rows=[('MyMonitor.lnk','C:/Tools/MyMonitor.lnk'),('VNC-A.vnc','C:/Remote/VNC-A.vnc'),('VNC-B.vnc','C:/Remote/VNC-B.vnc')]
    for i,(a,b) in enumerate(rows):
        y=250+i*78; text(d,(70,y),a,25,TEXT,False,'lm'); text(d,(520,y),b,22,MUTED,False,'lm'); d.line((55,y+38,1145,y+38),fill=LINE,width=2)
    for x,label,c in [(55,'Add...',BLUE),(205,'Edit...',BLUE),(355,'Remove','#B50000')]: d.rounded_rectangle((x,580,x+125,635),radius=8,fill=c); text(d,(x+62,607),label,20,WHITE,True,'mm')

def position(d):
    chrome(d,'TileKeeper  |  Position')
    text(d,(38,104),'Position',34,NAVY,True)
    d.rounded_rectangle((42,155,510,585),radius=10,fill=WHITE,outline=LINE,width=3); text(d,(75,190),'Screen 2',27,AMBER,True)
    for i,label in enumerate(['☑  VNC-A','☑  VNC-B','☐  Browser']):
        y=270+i*75; text(d,(80,y),label,26,TEXT,False,'lm')
    d.rounded_rectangle((575,155,1158,585),radius=10,fill=WHITE,outline=LINE,width=3); text(d,(866,190),'grid preview',25,MUTED,True,'mm')
    d.rectangle((620,245,1115,500),fill='#E9EDF3',outline=LINE,width=3)
    d.rectangle((630,255,865,490),fill=BLUE); d.rectangle((870,255,1105,490),fill=AMBER)
    text(d,(747,372),'1',70,WHITE,True,'mm'); text(d,(987,372),'2',70,WHITE,True,'mm')
    text(d,(64,625),'Check programs under the correct screen, then click Save.',24,NAVY,True)

def tray(d):
    chrome(d,'Windows desktop  |  notification area')
    d.rectangle((0,490,W,H),fill='#20252B'); text(d,(790,582),'^',30,WHITE,True,'mm'); text(d,(880,582),'12:45',25,WHITE,False,'mm')
    d.ellipse((1000,545,1060,605),fill=BLUE); text(d,(1030,575),'T',28,WHITE,True,'mm')
    d.rounded_rectangle((745,325,1080,515),radius=10,fill=WHITE,outline=LINE,width=3); text(d,(790,365),'TileKeeper',24,NAVY,True); text(d,(790,420),'Show',23,TEXT); text(d,(790,465),'Exit',23,TEXT)
    text(d,(600,155),'Right-click the TileKeeper tray icon',35,NAVY,True,'mm'); text(d,(600,210),'then choose Show.',30,BLUE,True,'mm')

def grid(d):
    chrome(d,'TileKeeper  |  two programs on Screen 2')
    text(d,(600,120),'AutoFit + Cols = 2',35,NAVY,True,'mm')
    d.rectangle((80,200,1120,570),fill='#20252B',outline=LINE,width=4)
    d.rectangle((95,215,595,555),fill=BLUE); d.rectangle((605,215,1105,555),fill=AMBER)
    text(d,(345,385),'VNC-A',46,WHITE,True,'mm'); text(d,(855,385),'VNC-B',46,WHITE,True,'mm')
    text(d,(600,625),'Each window fills one half of the screen.',25,MUTED,True,'mm')

assets=[save('01-tray.png',tray),save('02-programs.png',programs),save('03-position.png',position),save('04-grid.png',grid)]
asset_dir = ASSETS
prs=Presentation(str(PPT))
# Remove any previous embedded illustrations. Re-running this script must not stack
# duplicate pictures or preserve the old overlapping layout.
for slide in prs.slides:
    for shape in list(slide.shapes):
        if shape.shape_type == 13:  # PICTURE
            shape._element.getparent().remove(shape._element)

# Put illustrations on dedicated slides instead of squeezing them into instruction slides.
# This keeps the reading order clear: instruction first, visual example second.
def box(slide, text, x, y, w, h, size=18, color=RGBColor(27,39,51), bold=False, align=None, fill=None, line=None):
    if fill is not None:
        s=slide.shapes.add_shape(1, Inches(x), Inches(y), Inches(w), Inches(h))
        s.fill.solid(); s.fill.fore_color.rgb=fill; s.line.color.rgb=line or fill
    t=slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h)); tf=t.text_frame; tf.clear(); tf.word_wrap=True
    p=tf.paragraphs[0]; p.text=text; p.font.name='Aptos'; p.font.size=Pt(size); p.font.bold=bold; p.font.color.rgb=color
    if align is not None: p.alignment=align
    return t

def guide_slide(heading, instruction, image, caption, number):
    slide=prs.slides.add_slide(prs.slide_layouts[6])
    slide.background.fill.solid(); slide.background.fill.fore_color.rgb=RGBColor(243,245,248)
    box(slide, f'{number} / VISUAL EXAMPLE', .65, .3, 12, .3, 11, RGBColor(0,87,184), True)
    box(slide, heading, .65, .68, 12, .55, 29, RGBColor(20,35,55), True)
    box(slide, instruction, .8, 1.42, 11.7, .62, 18, RGBColor(27,39,51), True, fill=RGBColor(255,255,255), line=RGBColor(213,220,230))
    slide.shapes.add_picture(str(image), Inches(1.0), Inches(2.35), width=Inches(11.3), height=Inches(4.4))
    box(slide, caption, .8, 6.82, 11.7, .3, 11, RGBColor(75,90,107), False, align=PP_ALIGN.CENTER)

# These are intentionally standalone slides: no text or diagram is placed over the image.
guide_slide('Find TileKeeper in Windows', 'Right-click the TileKeeper icon near the clock, then choose Show.', asset_dir/'01-tray.png', 'The tray icon may be hidden behind the ^ arrow.', '17')
guide_slide('Add a program to watch', 'Open the Programs tab, click Add..., choose the program file, and confirm it appears in the list.', asset_dir/'02-programs.png', 'Supported examples include .exe, .lnk, .rdp, .vnc, and .appref-ms.', '18')
guide_slide('Assign a program to a monitor', 'Open Position, choose a screen, and check the program in that screen list.', asset_dir/'03-position.png', 'A program can be assigned to one screen at a time.', '19')
guide_slide('See the final tiled result', 'With AutoFit enabled and Cols set to 2, two programs fill the left and right halves of the screen.', asset_dir/'04-grid.png', 'The preview and the actual window placement use the same layout logic.', '20')
prs.save(str(PPT))
print(f'embedded 4 dedicated illustrations into {PPT}; slides={len(prs.slides)}')
