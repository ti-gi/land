"""
Printable A3 PDF of the extracted zones (overview, city-centre close-up, summary table).

    python render_pdf.py [--src out/zagreb_gup2025_namjena_EPSG3765.geojson] [--out out/zagreb_gup2025_namjena.pdf]
"""
import argparse, json, os, numpy as np, matplotlib
import pathlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from matplotlib.collections import PatchCollection
from matplotlib.patches import PathPatch
from matplotlib.path import Path
from collections import defaultdict
HERE=pathlib.Path(__file__).resolve().parent
ap=argparse.ArgumentParser()
ap.add_argument("--src",type=pathlib.Path,default=HERE/"out"/"zagreb_gup2025_namjena_EPSG3765.geojson")
ap.add_argument("--out",type=pathlib.Path,default=HERE/"out"/"zagreb_gup2025_namjena.pdf")
args=ap.parse_args()
SRC,OUT=args.src,args.out
gj=json.load(open(SRC,encoding="utf-8"))
COL={'S':'#f6e05e','M':'#f0a050','M0':'#e8923a','M1':'#f4b777','M2':'#e07b39','TR':'#c0763a',
'D':'#e06666','D1':'#d9534f','D2':'#e8838a','D3':'#c0392b','D4':'#f1948a','D5':'#ec7063','D6':'#b03a2e','D7':'#f5b7b1','D8':'#cd6155',
'I':'#8e44ad','K1':'#b04db0','K2':'#c06bc0','T':'#d98cd9','G':'#a569bd','N':'#5d4037',
'R':'#48c9b0','R1':'#48c9b0','R2':'#9be0d0','Z':'#a9dfbf','Z1':'#58d68d','Z2':'#1e8449','Z3':'#7dcea0','Z4':'#82e0aa','ZIS':'#b7e4c7','IS':'#7f8c8d',
'V':'#5dade2','V1':'#2e86c1','V2':'#5dade2','V3':'#85c1e9'}
def col(c): return COL.get(c,'#cccccc')
paths=[];codes=[];areas=[];bb=[]
for f in gj["features"]:
    g=f["geometry"]; polys=[g["coordinates"]] if g["type"]=="Polygon" else g["coordinates"]
    v=[];c=[]
    for poly in polys:
        for r in poly:
            r=np.asarray(r,float)[:,:2]; n=len(r)
            if n<3: continue
            cc=np.full(n,Path.LINETO,np.uint8); cc[0]=Path.MOVETO; cc[-1]=Path.CLOSEPOLY
            v.append(r);c.append(cc)
    if not v: continue
    V=np.concatenate(v); paths.append(Path(V,np.concatenate(c)))
    codes.append(f["properties"]["code"]); areas.append(f["properties"]["area_m2"])
    bb.append((V[:,0].min(),V[:,1].min(),V[:,0].max(),V[:,1].max()))
bb=np.array(bb); areas=np.array(areas)
order=np.argsort(-areas)
cnt=defaultdict(int);ar=defaultdict(float)
for c,a in zip(codes,areas): cnt[c]+=1; ar[c]+=a/1e6
cats=sorted(cnt,key=lambda c:-ar[c]); total=sum(ar.values())
def coll(idx): return PatchCollection([PathPatch(paths[i]) for i in idx],facecolor=[col(codes[i]) for i in idx],edgecolor=(0,0,0,.3),linewidth=.08)
def fmt(ax):
    f=plt.FuncFormatter(lambda v,p:f"{v/1000:,.0f}")
    ax.xaxis.set_major_formatter(f);ax.yaxis.set_major_formatter(f)
    ax.set_xlabel("E, HTRS96/TM (km)",fontsize=8);ax.set_ylabel("N, HTRS96/TM (km)",fontsize=8);ax.tick_params(labelsize=7)
def legend(fig,rect):
    la=fig.add_axes(rect);la.axis("off");la.text(0,1,"Zone code (GUP 2025)",fontsize=10,fontweight="bold",va="top")
    step=min(0.026,0.9/len(cats));y=0.96
    for c in cats:
        la.add_patch(plt.Rectangle((0,y-.018),.05,.016,facecolor=col(c),edgecolor="k",lw=.4,transform=la.transAxes))
        la.text(.07,y-.01,c,fontsize=8,va="center",transform=la.transAxes)
        la.text(1,y-.01,f"{cnt[c]:,}  |  {ar[c]:.2f} km²",fontsize=7.5,va="center",ha="right",color="#444",transform=la.transAxes)
        y-=step
NOTE="Source: GUP grada Zagreba, izmjene i dopune 2025., sheet 1 Korištenje i namjena prostora (1:5000), extracted from vector PDF. Streets are not zoned and appear white. Grey = unresolved code."
A3=(16.54,11.69)
with PdfPages(OUT) as pdf:
    pdf.infodict()["Title"]="Zagreb GUP 2025 - korištenje i namjena prostora"
    def page(title,sub,idx,xl,yl,extra=None):
        fig=plt.figure(figsize=A3)
        fig.text(.04,.965,title,fontsize=18,fontweight="bold",va="top");fig.text(.04,.935,sub,fontsize=10,color="#444",va="top")
        ax=fig.add_axes([.04,.06,.64,.86]);ax.add_collection(coll(idx));ax.set_xlim(*xl);ax.set_ylim(*yl);ax.set_aspect("equal");fmt(ax)
        legend(fig,[.71,.06,.27,.86]);fig.text(.04,.02,NOTE,fontsize=7,color="#555");pdf.savefig(fig);plt.close(fig)
    page("Zagreb – Generalni urbanistički plan, izmjene i dopune 2025.",f"{len(paths):,} polygons, {len(cats)} zone codes, HTRS96/TM (EPSG:3765)",order,(bb[:,0].min()-500,bb[:,2].max()+500),(bb[:,1].min()-500,bb[:,3].max()+500))
    cx,cy=461500,5074500; hw=4500
    x0,x1,y0,y1=cx-hw*1.45,cx+hw*1.45,cy-hw,cy+hw
    sel=[i for i in order if bb[i,2]>=x0 and bb[i,0]<=x1 and bb[i,3]>=y0 and bb[i,1]<=y1]
    page("City centre close-up",f"{len(sel):,} polygons in window",sel,(x0,x1),(y0,y1))
    fig=plt.figure(figsize=A3);fig.text(.04,.965,"Summary by zone code",fontsize=18,fontweight="bold",va="top")
    ax=fig.add_axes([.07,.10,.28,.80]);yy=np.arange(len(cats))[::-1]
    ax.barh(yy,[ar[c] for c in cats],color=[col(c) for c in cats],edgecolor="k",linewidth=.4)
    ax.set_yticks(yy);ax.set_yticklabels(cats,fontsize=8);ax.set_xlabel("Area (km²)");ax.spines[["top","right"]].set_visible(False)
    for yv,c in zip(yy,cats): ax.text(ar[c],yv,f" {ar[c]:.1f}",va="center",fontsize=7)
    ta=fig.add_axes([.42,.10,.55,.80]);ta.axis("off")
    rows=[[c,f"{cnt[c]:,}",f"{ar[c]:.2f}",f"{100*ar[c]/total:.1f}%"] for c in cats]+[["Total",f"{sum(cnt.values()):,}",f"{total:.2f}","100%"]]
    tb=ta.table(cellText=rows,colLabels=["Code","Polygons","Area km²","Share"],loc="upper center",cellLoc="left")
    tb.auto_set_font_size(False);tb.set_fontsize(8);tb.scale(1,1.2)
    for (r,c),cell in tb.get_celld().items():
        if r==0: cell.set_facecolor("#e8eef5");cell.set_text_props(fontweight="bold")
        cell.set_edgecolor("#bbb")
    fig.text(.04,.02,NOTE,fontsize=7,color="#555");pdf.savefig(fig);plt.close(fig)
print("ok",len(paths),"%.1f km2"%total)
