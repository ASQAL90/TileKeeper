from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE, MSO_CONNECTOR

OUT = 'docs/TileKeeper-architecture.pptx'
NAVY = RGBColor(20, 35, 55); BLUE = RGBColor(0, 87, 184); AMBER = RGBColor(230, 138, 0)
GREEN = RGBColor(31, 122, 76); LIGHT = RGBColor(243, 245, 248); INK = RGBColor(27, 39, 51)
MUTED = RGBColor(75, 90, 107); WHITE = RGBColor(255, 255, 255); LINE = RGBColor(213, 220, 230)
RED = RGBColor(181, 0, 0)
prs = Presentation(); prs.slide_width = Inches(13.333); prs.slide_height = Inches(7.5)
blank = prs.slide_layouts[6]

def box(slide, text, x, y, w, h, size=18, color=INK, bold=False, align=None, fill=None, line=None):
    if fill is not None:
        s = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(x), Inches(y), Inches(w), Inches(h))
        s.fill.solid(); s.fill.fore_color.rgb = fill; s.line.color.rgb = line or fill
    t = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf=t.text_frame; tf.clear(); tf.word_wrap=True; tf.margin_left=Inches(.12); tf.margin_right=Inches(.12); tf.vertical_anchor=MSO_ANCHOR.MIDDLE
    p=tf.paragraphs[0]; p.text=text; p.font.name='Aptos'; p.font.size=Pt(size); p.font.bold=bold; p.font.color.rgb=color
    if align is not None: p.alignment=align
    return t

def base(slide, heading, kicker=''):
    slide.background.fill.solid(); slide.background.fill.fore_color.rgb=LIGHT
    box(slide,kicker.upper(),.65,.25,12,.3,10,BLUE,True)
    box(slide,heading,.65,.62,12,.65,29,NAVY,True)

def footer(slide, n): box(slide,f'TileKeeper  •  beginner tutorial  •  {n}',.65,7.12,12,.2,9,MUTED)

def card(slide, heading, body, x,y,w,h,accent=BLUE):
    s=slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE,Inches(x),Inches(y),Inches(w),Inches(h)); s.fill.solid(); s.fill.fore_color.rgb=WHITE; s.line.color.rgb=LINE
    b=slide.shapes.add_shape(MSO_SHAPE.RECTANGLE,Inches(x),Inches(y),Inches(.1),Inches(h)); b.fill.solid(); b.fill.fore_color.rgb=accent; b.line.fill.background()
    box(slide,heading,x+.22,y+.12,w-.35,.42,15,NAVY,True); box(slide,body,x+.22,y+.58,w-.38,h-.68,12.5,MUTED)

def step(slide, n, heading, body, x, y, w=3.7, accent=BLUE):
    box(slide,str(n),x,y,.48,.48,20,WHITE,True,PP_ALIGN.CENTER,accent,accent)
    box(slide,heading,x+.62,y,w-.62,.42,16,NAVY,True)
    box(slide,body,x+.62,y+.48,w-.65,.9,13,MUTED)

def bullet_slide(heading,kicker,items,n):
    s=prs.slides.add_slide(blank); base(s,heading,kicker)
    y=1.45
    for i,item in enumerate(items,1):
        box(s,str(i),.9,y,.42,.42,16,WHITE,True,PP_ALIGN.CENTER,BLUE,BLUE); box(s,item,1.5,y,10.8,.62,16,INK)
        y += .82
    footer(s,n); return s

# 1
s=prs.slides.add_slide(blank); s.background.fill.solid(); s.background.fill.fore_color.rgb=NAVY
box(s,'TileKeeper',.8,1.0,11.8,.9,44,WHITE,True); box(s,'A simple guide for keeping programs open and windows organized',.85,2.05,11,.55,22,RGBColor(190,215,245)); box(s,'Start here if this is your first time using the computer program.',.85,2.85,10,.4,16,WHITE)
for x,c,label in [(8.2,BLUE,'1'),(9.7,AMBER,'2'),(11.2,GREEN,'3')]: box(s,label,x,4.35,1.15,1.15,30,WHITE,True,PP_ALIGN.CENTER,c,c)
box(s,'Windows tray application  •  .NET Framework 4.8',.85,6.7,11,.25,11,RGBColor(180,195,215)); footer(s,1)

# 2
s=prs.slides.add_slide(blank); base(s,'Before you begin','02 / important words');
card(s,'Program','An application you want to watch, such as a VNC viewer, browser, or monitoring tool.',.8,1.5,3.8,1.7,BLUE)
card(s,'Window','The rectangle you see on the screen when a program is open.',4.8,1.5,3.8,1.7,AMBER)
card(s,'Screen / monitor','A physical display connected to the computer. TileKeeper supports up to four.',8.8,1.5,3.75,1.7,GREEN)
card(s,'Grid cell / tile','One area of a screen. A 2-column grid creates a left tile and a right tile.',.8,3.8,5.8,1.7,BLUE)
card(s,'Tray icon','The small TileKeeper icon near the clock, usually at the bottom-right of Windows.',6.8,3.8,5.75,1.7,AMBER); footer(s,2)

# 3
s=prs.slides.add_slide(blank); base(s,'What TileKeeper does','03 / the big picture')
step(s,1,'You choose programs','Tell TileKeeper which programs matter.',.9,1.65,3.5,BLUE)
step(s,2,'It checks them','On a timer, it checks whether they are running.',4.9,1.65,3.5,AMBER)
step(s,3,'It fixes the layout','It relaunches closed programs and moves windows back to their assigned tiles.',8.9,1.65,3.5,GREEN)
box(s,'Think of TileKeeper as a caretaker: you set the rules once, then it keeps working in the background.',1.2,4.7,10.8,.8,20,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,3)

# 4
s=prs.slides.add_slide(blank); base(s,'Starting the program','04 / first launch')
for i,(h,b) in enumerate([('1. Find the file','Open TileKeeper.exe from the release folder.'),('2. Wait for the tray icon','The settings window may not open automatically. Look near the Windows clock.'),('3. Show hidden icons','If you do not see it, click the small ^ arrow near the clock.'),('4. Open settings','Right-click the TileKeeper icon and click Show.')]):
    step(s,i+1,h,b,.9+(i%2)*6.1,1.5+(i//2)*2.3,5.4,BLUE if i<2 else AMBER)
box(s,'Closing the settings window with X does not stop TileKeeper; it returns to the tray.',1.1,6.05,11,.5,16,INK,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,4)

# 5
s=prs.slides.add_slide(blank); base(s,'The settings window','05 / three tabs')
for x,label,desc,c in [(1,'PROGRAMS','What should be launched and kept running?',BLUE),(4.55,'POSITION','Which monitor and tile should each window use?',AMBER),(8.1,'SYSTEM','Optional display-resolution hold.',GREEN)]:
    box(s,label,x,1.7,3.1,.62,15,WHITE,True,PP_ALIGN.CENTER,c,c); box(s,desc,x,2.48,3.1,1.25,15,INK,False,PP_ALIGN.CENTER,WHITE,LINE)
box(s,'The Save button applies all changes. Clear removes all configured lists and should be used only when starting over.',1.1,4.7,11,.9,18,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,5)

# 6
s=prs.slides.add_slide(blank); base(s,'Add your first program','06 / programs tab')
step(s,1,'Click Add...','A file picker opens.',.9,1.45,5.5,BLUE); step(s,2,'Choose a file','Select an .exe, shortcut (.lnk), .rdp, .vnc, or .appref-ms file.',.9,2.75,5.5,BLUE); step(s,3,'Check the list','The new program appears as a row.',.9,4.05,5.5,BLUE)
box(s,'PROGRAMS',7.0,1.45,4.9,.5,14,WHITE,True,PP_ALIGN.CENTER,BLUE,BLUE)
for i,t in enumerate(['Program','MyMonitor.lnk','RemoteDesktop.rdp','VNC-A.vnc']): box(s,t,7.0,2.15+i*.62,4.9,.5,14,INK,i==0,PP_ALIGN.LEFT,WHITE,LINE)
box(s,'Use Edit... to replace a selected file. Use Remove to delete it. Up and Down change order.',7.0,5.1,4.9,.9,14,MUTED,False,PP_ALIGN.CENTER,WHITE,LINE); footer(s,6)

# 7
s=prs.slides.add_slide(blank); base(s,'Turn on Keep Running','07 / automatic restart')
box(s,'☐  Enable Keep Running',1.0,1.65,5.4,.7,21,INK,True,PP_ALIGN.LEFT,WHITE,LINE)
box(s,'Cycle (seconds):  20',1.0,2.65,5.4,.7,20,INK,False,PP_ALIGN.LEFT,WHITE,LINE)
card(s,'What happens?','Every 20 seconds TileKeeper checks each configured program. If one is closed, it starts it again.',7.0,1.65,4.9,2.1,BLUE)
card(s,'Suggested setting','15–30 seconds is a reasonable starting point. A lower number reacts faster but checks more often.',7.0,4.1,4.9,1.6,AMBER)
box(s,'Keep Running only launches programs from the Programs tab. Position-only entries are never launched.',1.0,6.15,11,.45,14,INK,True,PP_ALIGN.CENTER); footer(s,7)

# 8
s=prs.slides.add_slide(blank); base(s,'Assign a program to a screen','08 / position tab')
step(s,1,'Open Position','Click the Position tab.',.9,1.55,3.7,AMBER); step(s,2,'Find the screen','Use Screen 1, Screen 2, Screen 3, or Screen 4.',4.8,1.55,3.7,AMBER); step(s,3,'Check a program','Click its checkbox under the screen where it belongs.',8.7,1.55,3.7,AMBER)
box(s,'Screen 2',1.0,4.2,5.2,.5,15,WHITE,True,PP_ALIGN.CENTER,AMBER,AMBER)
for i,t in enumerate(['☑  VNC-A','☐  VNC-B','☐  Browser']): box(s,t,1.0,4.85+i*.52,5.2,.42,14,INK,False,PP_ALIGN.LEFT,WHITE,LINE)
box(s,'A program can be assigned to only one screen at a time. You can configure a disconnected screen and save it.',7.0,4.3,4.8,1.8,16,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,8)

# 9
s=prs.slides.add_slide(blank); base(s,'Choose the grid','09 / rows, columns, autofit')
card(s,'Rows','How many horizontal levels. Leave 0 for automatic.',.8,1.45,3.7,1.45,BLUE); card(s,'Cols','How many side-by-side columns. Set 2 for left and right.',4.8,1.45,3.7,1.45,AMBER); card(s,'AutoFit','Resize each window to fill its tile. Turn off to move without resizing.',8.8,1.45,3.7,1.45,GREEN)
box(s,'Example: Cols = 2',1.0,3.55,3.2,.5,15,WHITE,True,PP_ALIGN.CENTER,BLUE,BLUE)
for x,c,t in [(1.0,BLUE,'LEFT'),(4.25,AMBER,'RIGHT')]: box(s,t,x,4.2,3.0,1.65,20,WHITE,True,PP_ALIGN.CENTER,c,c)
box(s,'Maximum grid size is 3 × 3 = 9 cells per screen. Saving fails if there are more programs than available cells.',8.0,4.0,4.0,1.4,15,RED,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,9)

# 10
s=prs.slides.add_slide(blank); base(s,'Borderless and border mode','10 / window appearance')
card(s,'Borderless checked','Removes the title bar and frame. Windows touch edge-to-edge, which is useful for dashboards and monitoring displays.',.9,1.6,5.6,2.0,BLUE)
card(s,'Borderless unchecked','Keeps a title bar and thin frame. Windows still stay flush with neighboring tiles.',6.9,1.6,5.6,2.0,AMBER)
box(s,'Start with the default Borderless setting. Use border mode when you need a normal title bar. Some fixed-size applications may clip content in border mode.',1.0,4.55,11.1,.95,17,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,10)

# 11
s=prs.slides.add_slide(blank); base(s,'Pin a program to a specific cell','11 / optional control')
for i,(x,c,t) in enumerate([(1.0,BLUE,'1'),(4.15,AMBER,'2'),(7.3,GREEN,'3'),(10.45,RGBColor(123,31,162),'4')]): box(s,t,x,1.65,2.2,1.6,28,WHITE,True,PP_ALIGN.CENTER,c,c)
step(s,1,'Click a grid cell','A small menu appears.',1.0,4.0,3.4,BLUE); step(s,2,'Select a program','That program is locked to the cell.',4.7,4.0,3.4,AMBER); step(s,3,'To undo','Choose Auto-fill (clear pin).',8.4,4.0,3.4,GREEN); footer(s,11)

# 12
s=prs.slides.add_slide(blank); base(s,'A complete first-time setup','12 / follow these steps')
items=['Open TileKeeper and right-click the tray icon → Show.','Programs tab → Add... → choose your program file.','Check Enable Keep Running and set Cycle to 20 seconds.','Position tab → check the program under the correct screen.','Set Cols/Rows and check AutoFit if you want it resized.','Leave Borderless checked unless you need a title bar.','Click Save, then open or wait for the program.']
y=1.4
for i,t in enumerate(items,1): box(s,str(i),.9,y,.4,.4,15,WHITE,True,PP_ALIGN.CENTER,BLUE,BLUE); box(s,t,1.45,y,10.8,.48,15,INK); y+=.68
footer(s,12)

# 13
s=prs.slides.add_slide(blank); base(s,'Example: two VNC connections','13 / practical walkthrough')
items=['Create two shortcuts to the same VNC viewer. Give each shortcut a different address.','Add both shortcuts on the Programs tab.','Enable Keep Running.','On Position, check both under Screen 2.','Set Cols = 2 and AutoFit = checked.','Click Save. Each connection is relaunched independently.']
y=1.35
for i,t in enumerate(items,1): box(s,str(i),.9,y,.4,.4,15,WHITE,True,PP_ALIGN.CENTER,AMBER,AMBER); box(s,t,1.45,y,10.8,.5,15,INK); y+=.72
box(s,'Result: VNC-A stays on the left; VNC-B stays on the right.',1.1,5.95,11,.55,19,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,13)

# 14
s=prs.slides.add_slide(blank); base(s,'When something goes wrong','14 / simple fixes')
rows=[('Program never launches','Keep Running is enabled and the selected file still exists.'),('Window goes to the wrong monitor','Enable Custom Position, assign the right screen, and click Save.'),('Wrong window moves','Enable Match Largest Window for that program.'),('Screen controls are gray','That monitor is disconnected; reconnecting it re-enables the controls.'),('Bottom content is clipped','Try Borderless mode instead of border mode.'),('Need to stop TileKeeper','Right-click the tray icon and choose Exit.')]
for i,(a,b) in enumerate(rows): y=1.35+i*.78; box(s,a,.85,y,3.4,.48,13,NAVY,True,fill=WHITE,line=LINE); box(s,b,4.45,y,7.8,.48,13,MUTED,False,fill=WHITE,line=LINE)
footer(s,14)

# 15
s=prs.slides.add_slide(blank); base(s,'How the code is organized','15 / for maintainers')
files=[('Program.cs','Startup and single-instance protection'),('TileKeeperForm.cs','Tray UI, tabs, timers, settings'),('Class/WindowMonitor.cs','Process matching and Win32 placement'),('Class/GridMapPanel.cs','Preview of the same tile layout'),('Class/Win32ApiImpl.cs','Native Windows API implementation'),('Tests/','NUnit tests for layout and behavior'),('docs/','Guides and this presentation')]
for i,(f,d) in enumerate(files): col=i//4; row=i%4; x=.9+col*6.1; y=1.35+row*1.1; box(s,f,x,y,2.45,.4,14,BLUE,True); box(s,d,x+2.25,y,3.6,.4,12,MUTED)
box(s,'The important idea: the settings preview and the runtime use the same layout calculations, so what you configure is what the app applies.',1.0,6.0,11,.55,16,NAVY,True,PP_ALIGN.CENTER,WHITE,LINE); footer(s,15)

# 16
s=prs.slides.add_slide(blank); base(s,'Final checklist','16 / you are ready')
for i,t in enumerate(['TileKeeper is visible in the system tray.','Your program appears on the Programs tab.','Keep Running is enabled if automatic relaunch is needed.','The program is checked under the correct screen.','Rows, Cols, AutoFit, and Borderless match your needs.','You clicked Save.']): box(s,'☐  '+t,.95,1.45+i*.72,11,.5,17,INK,False,fill=WHITE,line=LINE)
box(s,'For the full reference, open docs/USER_GUIDE.md or docs/user-guide.html.',1.0,6.1,11,.5,17,BLUE,True,PP_ALIGN.CENTER); footer(s,16)

prs.save(OUT); print(OUT, len(prs.slides))
