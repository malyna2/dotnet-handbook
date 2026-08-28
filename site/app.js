/* ============================================================
   The Middle → Senior .NET Developer Handbook — reader app
   Self-contained: custom Markdown renderer, highlighter,
   navigation, search, and select-to-translate.
   ============================================================ */
(function () {
"use strict";
var BOOK = window.BOOK || [];
var bySlug = {};
BOOK.forEach(function (c, i) { c.index = i; bySlug[c.slug] = c; });

// The id render() gives a heading. Kept in one place so the section index below and the
// rendered DOM can never disagree about what "#some-heading" refers to.
function headingSlug(txt){
  return (txt.toLowerCase().replace(/[^\w\s-]/g,"").replace(/[\s_]+/g,"-").replace(/^-+|-+$/g,""))||"section";
}
// Section id -> the chapter that contains it, so a link such as "#exception-handling-strategy"
// can open the right chapter *and* land on that section. Ids that occur in more than one
// chapter (there are a handful, e.g. "summary") are left out rather than guessed at.
var headingOwner=(function(){
  var owner={}, dupe={};
  BOOK.forEach(function(c){
    var used={};
    (c.md.match(/^#{1,6}[ \t]+.*$/gm)||[]).forEach(function(line){
      var base=headingSlug(line.replace(/^#{1,6}[ \t]+/,"").trim());
      used[base]=(used[base]||0)+1;
      var id=used[base]===1?base:base+"-"+used[base];
      if(owner.hasOwnProperty(id)){ if(owner[id]!==c.slug) dupe[id]=1; }
      else owner[id]=c.slug;
    });
  });
  Object.keys(dupe).forEach(function(k){ delete owner[k]; });
  return owner;
})();

var S0 = String.fromCharCode(1), S1 = String.fromCharCode(2); // inline-code placeholders

/* ---------------- Markdown → HTML ---------------- */
function esc(s){return s.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;");}
function escAttr(s){return esc(s).replace(/"/g,"&quot;");}

function inline(text){
  var codes=[];
  text=text.replace(/`([^`]+)`/g,function(_,c){codes.push(c);return S0+(codes.length-1)+S1;});
  text=esc(text);
  // links [t](u)
  text=text.replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g,function(_,t,u){
    return '<a href="'+escAttr(u)+'"'+(/^https?:/.test(u)?' target="_blank" rel="noopener"':'')+'>'+t+'</a>';
  });
  // bold, italic, strikethrough
  text=text.replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>');
  text=text.replace(/(^|[^\w*])\*([^*\n]+)\*(?!\w)/g,'$1<em>$2</em>');
  text=text.replace(/(^|[^\w_])_([^_\n]+)_(?!\w)/g,'$1<em>$2</em>');
  text=text.replace(/~~([^~]+)~~/g,'<del>$1</del>');
  // restore inline code
  text=text.replace(new RegExp(S0+"(\\d+)"+S1,"g"),function(_,i){return '<code>'+esc(codes[+i])+'</code>';});
  return text;
}

/* ---------------- Syntax highlighting (single-pass tokenizer) ---------------- */
var CS_KW=("abstract as async await base bool break byte case catch char checked class const continue "+
  "decimal default delegate do double else enum event explicit extern false finally fixed float for foreach "+
  "get goto if implicit in int interface internal is lock long namespace new null object operator out override "+
  "params private protected public readonly record ref return sbyte sealed set short sizeof stackalloc static "+
  "string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void "+
  "volatile while yield with when nameof and or not init required scoped file global dynamic value from where "+
  "select group into orderby let join on equals by ascending descending await Task").trim().split(/\s+/).join("|");

var CODE_RE=new RegExp(
  "(\\/\\/[^\\n]*|\\/\\*[\\s\\S]*?\\*\\/)"+                                                    // 1 comment
  "|(@\\$?\"(?:[^\"]|\"\")*\"|\\$?\"(?:\\\\.|[^\"\\\\\\n])*\"|'(?:\\\\.|[^'\\\\\\n])*')"+       // 2 string
  "|(\\b0[xX][0-9a-fA-F]+\\b|\\b\\d[\\d_]*(?:\\.\\d+)?(?:[eE][+-]?\\d+)?[fFdDmMlLuU]*\\b)"+     // 3 number
  "|(\\b(?:"+CS_KW+")\\b)"+                                                                     // 4 keyword
  "|([A-Za-z_][A-Za-z0-9_]*)(?=\\s*\\()"+                                                       // 5 fn call
  "|(\\b[A-Z][A-Za-z0-9_]*\\b)","g");                                                           // 6 type
var SHELL_RE=/(#[^\n]*)|("(?:\\.|[^"\\\n])*"|'(?:[^'\n])*')|(\b\d[\d.]*\b)/g;
var MIN_RE=/("(?:\\.|[^"\\\n])*")|(\b-?\d[\d.]*\b)/g;
var CODE_CLASSES=["tok-com","tok-str","tok-num","tok-kw","tok-fn","tok-type"];
var SHELL_CLASSES=["tok-com","tok-str","tok-num"];
var MIN_CLASSES=["tok-str","tok-num"];
var LANG_SHELL=/^(bash|sh|shell|console|zsh|dockerfile|docker|yaml|yml|ini|toml|makefile|make|powershell|ps1|nginx|conf|properties|env|diff|http)$/;
var LANG_CODE=/^(csharp|cs|c#|c|cpp|java|javascript|js|jsx|typescript|ts|tsx|go|golang|rust|rs|python|py|php|kotlin|swift|scala|sql|razor|cshtml)$/;

function scan(code, re, classes){
  var out="",last=0,m; re.lastIndex=0;
  while((m=re.exec(code))){
    if(m.index>last) out+=esc(code.slice(last,m.index));
    var cls=null;
    for(var g=1; g<m.length; g++){ if(m[g]!=null){ cls=classes[g-1]; break; } }
    out += cls ? '<span class="'+cls+'">'+esc(m[0])+'</span>' : esc(m[0]);
    last=m.index+m[0].length;
    if(m[0].length===0) re.lastIndex++;
  }
  out+=esc(code.slice(last));
  return out;
}
function highlight(code, lang){
  lang=(lang||"").toLowerCase();
  if(LANG_SHELL.test(lang)) return scan(code,SHELL_RE,SHELL_CLASSES);
  if(LANG_CODE.test(lang))  return scan(code,CODE_RE,CODE_CLASSES);
  return scan(code,MIN_RE,MIN_CLASSES);
}

var usedIds={};
function render(md){
  var lines=md.replace(/\r\n/g,"\n").split("\n");
  var html=[], i=0;
  function flushPara(buf){ if(buf.length){ html.push("<p>"+inline(buf.join(" "))+"</p>"); } }
  while(i<lines.length){
    var line=lines[i];
    var fm=line.match(/^```(\w*)/);
    if(fm){
      var lang=fm[1]||"", code=[]; i++;
      while(i<lines.length && !/^```/.test(lines[i])){ code.push(lines[i]); i++; }
      i++;
      var body=highlight(code.join("\n"),lang);
      html.push('<div class="codeblock">'+(lang?'<span class="lang">'+esc(lang)+'</span>':'')+
        '<button class="copy" type="button">Copy</button><pre><code>'+body+'</code></pre></div>');
      continue;
    }
    var hm=line.match(/^(#{1,6})\s+(.*)$/);
    if(hm){
      var lvl=hm[1].length, txt=hm[2].trim();
      var base=headingSlug(txt);
      usedIds[base]=(usedIds[base]||0)+1;
      var slug=usedIds[base]===1?base:base+"-"+usedIds[base];
      html.push("<h"+lvl+' id="'+slug+'">'+inline(txt)+"</h"+lvl+">");
      i++; continue;
    }
    if(/^(-{3,}|\*{3,}|_{3,})\s*$/.test(line)){ html.push("<hr>"); i++; continue; }
    // Collapsible blocks: a small, deliberate raw-HTML allowlist so exercise
    // answers can be hidden. Everything else stays escaped (see inline()).
    var sm=line.trim().match(/^<summary>(.*)<\/summary>$/i);
    if(sm){ html.push("<summary>"+inline(sm[1])+"</summary>"); i++; continue; }
    if(/^<\/?(details|summary)(\s[^>]*)?>$/i.test(line.trim())){ html.push(line.trim()); i++; continue; }
    if(/^\s*\|.*\|\s*$/.test(line) && i+1<lines.length && /^\s*\|?[\s:|-]+\|[\s:|-]*$/.test(lines[i+1]) && lines[i+1].indexOf("-")>=0){
      var head=splitRow(line);
      i+=2;
      var rows=[];
      while(i<lines.length && /^\s*\|.*\|\s*$/.test(lines[i])){ rows.push(splitRow(lines[i])); i++; }
      var t='<div class="table-wrap"><table><thead><tr>';
      head.forEach(function(h){t+="<th>"+inline(h)+"</th>";});
      t+="</tr></thead><tbody>";
      rows.forEach(function(r){t+="<tr>";head.forEach(function(_,ci){t+="<td>"+inline(r[ci]||"")+"</td>";});t+="</tr>";});
      t+="</tbody></table></div>"; html.push(t); continue;
    }
    if(/^>\s?/.test(line)){
      var q=[];
      while(i<lines.length && /^>\s?/.test(lines[i])){ q.push(lines[i].replace(/^>\s?/,"")); i++; }
      html.push("<blockquote>"+render(q.join("\n"))+"</blockquote>");
      continue;
    }
    if(/^\s*([-*+]|\d+\.)\s+/.test(line)){
      var listHtml=parseList(lines,i);
      html.push(listHtml.html); i=listHtml.next; continue;
    }
    if(/^\s*$/.test(line)){ i++; continue; }
    var para=[];
    while(i<lines.length && !/^\s*$/.test(lines[i]) && !/^```/.test(lines[i]) &&
          !/^(#{1,6})\s/.test(lines[i]) && !/^>\s?/.test(lines[i]) &&
          !/^\s*([-*+]|\d+\.)\s+/.test(lines[i]) && !/^\s*\|.*\|\s*$/.test(lines[i]) &&
          !/^(-{3,}|\*{3,})\s*$/.test(lines[i]) &&
          !/^<\/?(details|summary)(\s[^>]*)?>/i.test(lines[i].trim())){
      para.push(lines[i]); i++;
    }
    flushPara(para);
  }
  return html.join("\n");
}
function splitRow(line){
  var s=line.trim().replace(/^\|/,"").replace(/\|$/,"");
  var cells=[],cur="",j=0;
  for(;j<s.length;j++){ if(s[j]==="\\"&&s[j+1]==="|"){cur+="|";j++;} else if(s[j]==="|"){cells.push(cur.trim());cur="";} else cur+=s[j]; }
  cells.push(cur.trim()); return cells;
}
function indentOf(s){var m=s.match(/^(\s*)/);return m[1].replace(/\t/g,"  ").length;}
function parseList(lines,start){
  var baseIndent=indentOf(lines[start]);
  var ordered=/^\s*\d+\.\s/.test(lines[start]);
  var out="<"+(ordered?"ol":"ul")+">";
  var i=start;
  while(i<lines.length){
    var line=lines[i];
    if(/^\s*$/.test(line)){
      if(i+1<lines.length && /^\s*([-*+]|\d+\.)\s+/.test(lines[i+1]) && indentOf(lines[i+1])>=baseIndent){ i++; continue; }
      break;
    }
    if(!/^\s*([-*+]|\d+\.)\s+/.test(line)) break;
    var ind=indentOf(line);
    if(ind<baseIndent) break;
    if(ind>baseIndent){
      var nested=parseList(lines,i);
      out=out.replace(/<\/li>$/,"")+nested.html+"</li>";
      i=nested.next; continue;
    }
    var content=line.replace(/^\s*([-*+]|\d+\.)\s+/,"");
    out+="<li>"+inline(content)+"</li>";
    i++;
  }
  out+="</"+(ordered?"ol":"ul")+">";
  return {html:out,next:i};
}

/* ---------------- Translation (MyMemory) ---------------- */
var settings={ email: localStorage.getItem("tr_email")||"", lang: localStorage.getItem("tr_lang")||"uk", easy: localStorage.getItem("tr_easy")==="1" };
function cacheKey(t){return "tr:"+settings.lang+":"+t;}
function getCached(t){try{return localStorage.getItem(cacheKey(t));}catch(e){return null;}}
function setCached(t,v){try{localStorage.setItem(cacheKey(t),v);}catch(e){}}
function translate(text){
  text=text.trim();
  if(!text) return Promise.resolve("");
  var hit=getCached(text);
  if(hit!==null) return Promise.resolve(hit);
  var url="https://api.mymemory.translated.net/get?q="+encodeURIComponent(text)+
          "&langpair="+encodeURIComponent("en|"+settings.lang);
  if(settings.email) url+="&de="+encodeURIComponent(settings.email);
  return fetch(url).then(function(r){return r.json();}).then(function(d){
    var out=(d&&d.responseData&&d.responseData.translatedText)||"";
    if(/MYMEMORY WARNING|QUERY LENGTH LIMIT|YOU USED ALL AVAILABLE/i.test(out)){
      throw new Error("Daily translation limit reached — add your email in 🇺🇦 settings for a higher limit.");
    }
    if(!out) throw new Error("No translation returned.");
    setCached(text,out);
    return out;
  });
}

/* ---------------- App shell / routing ---------------- */
var content=document.getElementById("content");
var navEl=document.getElementById("nav");
var outlineEl=document.getElementById("outline");
var current=null;
var pendingFind=null, pendingResume=null, pendingSection=null;
var _saveT;
// Per-chapter reading *progress* — a percent, a sticky "finished" flag, and the index of
// the block you had reached — drives the sidebar bars, ✓ marks, and the Continue button.
// Nothing here is restored automatically: chapters always open at the top, and you return
// to where you got to only by asking for it. The stored block index is kept in step with
// the high-water percent, so Continue always lands at the point the bar is showing.
function topBlockIndex(){
  var kids=content.children;
  for(var i=0;i<kids.length;i++){
    if(kids[i].getBoundingClientRect().bottom>100) return i;
  }
  return 0;
}
function readPct(){
  var h=document.documentElement;
  var max=h.scrollHeight-h.clientHeight;
  return max>0?Math.min(100,Math.round(h.scrollTop/max*100)):0;
}
function saveProgress(){ if(!current) return; try{
  var prev=savedPos(current.slug), was=(prev&&typeof prev.p==="number")?prev.p:0;
  var now=readPct();
  // Progress is one-way: it records how far you have got, not where you are now.
  // Scrolling back up — or reopening a chapter at the top, which is what always
  // happens — must never wind the bar backwards.
  var p=Math.max(now, was);
  // Only advance the resume point when the bar advances, so the two never disagree.
  var i=(now>=was||!prev||typeof prev.i!=="number")?topBlockIndex():prev.i;
  // "finished" is sticky too: once a chapter has been read to the end, it stays ✓
  var done=((prev&&prev.d)||p>=97)?1:0;
  localStorage.setItem("pos:"+current.slug, JSON.stringify({p:p, d:done, i:i}));
  updateNavProgress(current.slug);
}catch(e){} }
function hasProgress(slug){ var d=savedPos(slug); return !!(d&&typeof d.p==="number"&&d.p>0); }
// Scroll to the stored resume point. Only ever called for an explicit Continue.
function resumeTo(c){
  var d=savedPos(c.slug); if(!d) return;
  if(typeof d.i==="number" && d.i>0 && d.i<content.children.length){
    var el=content.children[d.i];
    window.scrollTo(0, el.getBoundingClientRect().top+window.pageYOffset-90);
  }
}
function resetProgress(slug){
  try{ localStorage.removeItem("pos:"+slug); }catch(e){}
  // If it is the chapter on screen, go back to the top too — otherwise the next scroll
  // event would immediately re-record the position you just cleared.
  if(current && current.slug===slug) window.scrollTo(0,0);
  updateNavProgress(slug);
  toast("Progress reset");
}
function isDone(slug){ var d=savedPos(slug); return !!(d&&(d.d||d.p>=97)); }
function scheduleSave(){ clearTimeout(_saveT); _saveT=setTimeout(saveProgress,400); }
function savedPos(slug){
  var raw; try{ raw=localStorage.getItem("pos:"+slug); }catch(e){ return null; }
  if(!raw) return null;
  var d; try{ d=JSON.parse(raw); }catch(e){ return null; }
  if(d && typeof d==="object") return d;
  return null;                                     // legacy pixel-offset format: ignore
}

function readTime(md){
  var m=md.match(/Estimated read time:\s*~\s*(\d+\s*h(?:\s*\d+\s*min)?|\d+\s*min)/i);
  return m?("~"+m[1].replace(/\s+/g," ").trim()):"";
}
function buildNav(){
  var html="", lastPart=null;
  BOOK.forEach(function(c){
    if(c.part==="__home__") return;
    if(c.part!==lastPart){ html+='<div class="part">'+esc(c.part)+"</div>"; lastPart=c.part; }
    var rt=readTime(c.md);
    var numMatch=c.title.match(/^(Chapter\s+\d+|Appendix\s+[A-Z])/);
    var num=numMatch?numMatch[1].replace("Chapter ","").replace("Appendix ","App "):"";
    html+='<a href="#/'+c.slug+'" data-slug="'+c.slug+'"><span class="num">'+esc(num)+
      '</span>'+esc(c.nav)+(rt?'<span class="rt">'+rt+'</span>':'')+navActs(c.slug)+'</a>';
  });
  navEl.innerHTML=html;
  BOOK.forEach(function(c){ if(c.part!=="__home__") updateNavProgress(c.slug); });
}
// Continue / Reset, shown only once a chapter has progress to act on. These live inside
// the nav <a>, so their handlers must stop the click from also following the link.
var ICON_RESUME='<svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" '+
  'stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M2.5 8h9"/><path d="M8 4.5 11.5 8 8 11.5"/></svg>';
var ICON_RESET='<svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" '+
  'stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M13.5 8a5.5 5.5 0 1 1-1.6-3.9"/><path d="M13.6 2.2v3.1h-3.1"/></svg>';
function navActs(slug){
  return '<span class="nav-acts">'+
    '<span class="nav-act" role="button" tabindex="0" data-act="resume" data-slug="'+slug+
      '" title="Continue where you got to" aria-label="Continue where you got to">'+ICON_RESUME+'</span>'+
    '<span class="nav-act" role="button" tabindex="0" data-act="reset" data-slug="'+slug+
      '" title="Reset progress" aria-label="Reset progress">'+ICON_RESET+'</span></span>';
}
navEl.addEventListener("click",function(ev){
  var btn=ev.target.closest&&ev.target.closest(".nav-act"); if(!btn) return;
  ev.preventDefault(); ev.stopPropagation();          // do not also follow the chapter link
  var slug=btn.getAttribute("data-slug");
  if(btn.getAttribute("data-act")==="resume"){ pendingResume=slug; navigate(slug); }
  else resetProgress(slug);
});
navEl.addEventListener("keydown",function(ev){
  if(ev.key!=="Enter" && ev.key!==" ") return;
  var btn=ev.target.closest&&ev.target.closest(".nav-act"); if(!btn) return;
  ev.preventDefault(); btn.click();
});
function updateNavProgress(slug){
  var a=navEl.querySelector('a[data-slug="'+slug+'"]'); if(!a) return;
  var d=savedPos(slug);
  var p=d&&typeof d.p==="number"?d.p:0;
  a.style.setProperty("--rp", p+"%");
  a.classList.toggle("done", isDone(slug));
  a.classList.toggle("has-progress", hasProgress(slug));
}
// Assigning an unchanged location.hash fires no hashchange event, so a link to the
// chapter you are already reading would silently do nothing. Route every in-app
// navigation through here so that case still re-renders. Pass a section id to land on
// that heading instead of the top of the chapter.
function navigate(slug, sec){
  pendingSection=sec?{slug:slug, sec:sec}:null;
  var target="#/"+slug;
  if(location.hash===target) go(slug,false);
  else location.hash=target;
}
function scrollToId(id){
  var el=document.getElementById(id); if(!el) return false;
  window.scrollTo(0, el.getBoundingClientRect().top+window.pageYOffset-70);
  return true;
}
function go(slug, push){
  saveProgress();
  var c=bySlug[slug]||BOOK[0];
  current=c;
  removePopup();
  usedIds={};
  content.innerHTML=render(c.md);
  postProcess(c);
  buildOutline(c);
  buildPager(c);
  highlightNav(c.slug);
  window.scrollTo(0,0);             // every chapter opens at the beginning
  // ...unless we were asked for somewhere specific: a Continue click, or a link that
  // named a section rather than a whole chapter.
  if(pendingResume===c.slug) resumeTo(c);
  else if(pendingSection && pendingSection.slug===c.slug) scrollToId(pendingSection.sec);
  pendingResume=null; pendingSection=null;
  document.title=(c.nav?c.nav+" · ":"")+".NET Handbook";
  closeSidebar();
  if(push!==false) history.replaceState(null,"","#/"+c.slug);
  if(pendingFind){ highlightFind(pendingFind); pendingFind=null; }
}
function highlightNav(slug){
  navEl.querySelectorAll("a").forEach(function(a){a.classList.toggle("active",a.dataset.slug===slug);});
}
function postProcess(c){
  content.querySelectorAll(".codeblock .copy").forEach(function(btn){
    btn.addEventListener("click",function(){
      var code=btn.parentNode.querySelector("code");
      var txt=code.innerText;
      if(navigator.clipboard) navigator.clipboard.writeText(txt);
      btn.textContent="Copied!"; setTimeout(function(){btn.textContent="Copy";},1200);
    });
  });
  if(c.part==="__home__"){
    var grid=document.createElement("div"); grid.className="home-grid";
    BOOK.forEach(function(ch){
      if(ch.part==="__home__") return;
      var rt=readTime(ch.md);
      var numMatch=ch.title.match(/^(Chapter\s+\d+|Appendix\s+[A-Z])/);
      var a=document.createElement("a"); a.className="home-card"+(isDone(ch.slug)?" done":""); a.href="#/"+ch.slug;
      a.innerHTML='<div class="hc-num">'+esc(numMatch?numMatch[1]:"")+'</div>'+
        '<div class="hc-ttl">'+esc(ch.nav)+'</div>'+(rt?'<div class="hc-rt">⏱️ '+rt+'</div>':'');
      grid.appendChild(a);
    });
    var h=document.createElement("h2"); h.textContent="Browse chapters";
    content.appendChild(h); content.appendChild(grid);
  }
  // Decorate first: release links get their own handler and are skipped below, so a click
  // never runs two navigations.
  if(WN && c.id===WN.id){ wnDecorate(content); wnMarkSeen(); }
  content.querySelectorAll('a[href^="#"]').forEach(function(a){
    var href=a.getAttribute("href");
    if(href.indexOf("#/")===0 || a.classList.contains("wn-link")) return;
    var slug=href.slice(1);
    a.addEventListener("click",function(ev){
      if(bySlug[slug]){ ev.preventDefault(); navigate(slug); return; }
      var target=document.getElementById(slug);
      if(target){ ev.preventDefault(); target.scrollIntoView(); return; }
      // Not on this page — but if exactly one chapter has that heading, go there.
      var owner=headingOwner[slug];
      if(owner){ ev.preventDefault(); navigate(owner, slug); }
    });
  });
}
function buildOutline(c){
  var hs=content.querySelectorAll("h2, h3");
  if(!hs.length){ outlineEl.innerHTML=""; return; }
  var html='<div class="ol-title">On this page</div>';
  hs.forEach(function(h){ if(!h.id)h.id=Math.random().toString(36).slice(2);
    html+='<a href="#'+h.id+'" class="'+(h.tagName==="H3"?"h3":"h2")+'" data-id="'+h.id+'">'+
      esc(h.textContent)+"</a>"; });
  outlineEl.innerHTML=html;
  outlineEl.querySelectorAll("a").forEach(function(a){
    a.addEventListener("click",function(ev){ev.preventDefault();
      closeSidebar();          // on mobile the outline lives in the drawer — get out of the way
      var t=document.getElementById(a.dataset.id); if(t)t.scrollIntoView();});
  });
}
function buildPager(c){
  var list=BOOK.filter(function(x){return x.part!=="__home__";});
  var idx=list.indexOf(c);
  var prev=idx>0?list[idx-1]:null, next=idx>=0&&idx<list.length-1?list[idx+1]:null;
  var p=document.getElementById("pager"); p.innerHTML="";
  if(prev)p.innerHTML+='<a class="prev" href="#/'+prev.slug+'"><span class="lbl">← Previous</span><span class="ttl">'+esc(prev.nav)+'</span></a>';
  else p.innerHTML+='<span></span>';
  if(next)p.innerHTML+='<a class="next" href="#/'+next.slug+'"><span class="lbl">Next →</span><span class="ttl">'+esc(next.nav)+'</span></a>';
}

/* ---------------- Search ---------------- */
var searchBox=document.getElementById("search");
var searchResults=document.getElementById("searchResults");
function runSearch(q){
  q=q.trim().toLowerCase();
  if(q.length<2){ searchResults.hidden=true; return; }
  var res=[];
  BOOK.forEach(function(c){
    if(c.part==="__home__") return;
    var titleHit=c.title.toLowerCase().indexOf(q)>=0;
    var idx=c.md.toLowerCase().indexOf(q);
    if(titleHit||idx>=0){
      var snippet="";
      if(idx>=0){ var s=Math.max(0,idx-40); snippet=c.md.slice(s,idx+60).replace(/\n/g," "); }
      res.push({c:c,score:(titleHit?0:1),snippet:snippet});
    }
  });
  res.sort(function(a,b){return a.score-b.score;});
  res=res.slice(0,12);
  if(!res.length){ searchResults.innerHTML='<a>No results</a>'; searchResults.hidden=false; return; }
  var rq=q.replace(/[.*+?^${}()|[\]\\]/g,"\\$&");
  searchResults.innerHTML=res.map(function(r){
    var snip=esc(r.snippet).replace(new RegExp("("+rq+")","ig"),"<mark>$1</mark>");
    return '<a href="#/'+r.c.slug+'"><div>'+esc(r.c.nav)+'</div>'+
      (snip?'<div class="sr-ch">…'+snip+'…</div>':'')+'</a>';
  }).join("");
  searchResults.hidden=false;
  searchResults.querySelectorAll("a").forEach(function(a){
    a.addEventListener("click",function(ev){
      ev.preventDefault();
      var slug=a.getAttribute("href").slice(2);
      pendingFind=q; searchResults.hidden=true; searchBox.value="";
      if(current && current.slug===slug){ highlightFind(pendingFind); pendingFind=null; }
      else location.hash="#/"+slug;
    });
  });
}
searchBox.addEventListener("input",function(){runSearch(this.value);});
searchBox.addEventListener("focus",function(){if(this.value)runSearch(this.value);});
document.addEventListener("click",function(e){
  if(!e.target.closest(".search-wrap")) searchResults.hidden=true;
});

/* ---------------- Select-to-translate → popup over the selection ---------------- */
var selBtn=document.getElementById("selTranslate");
var lastSel="", lastRect=null, popup=null, pendingText="";
function positionSelBtn(vx, vy){
  selBtn.style.left=(vx+window.scrollX)+"px";
  selBtn.style.top=(vy+window.scrollY)+"px";
}
function removePopup(){ if(popup){popup.remove();popup=null;} }
function rectFromSelection(){
  var sel=window.getSelection();
  if(sel && sel.rangeCount){ var r=sel.getRangeAt(0).getBoundingClientRect(); if(r.width||r.height) return r; }
  return null;
}

function splitSentencesSmart(text){
  var res=[], start=0, re=/[.!?]+["'’”)\]]*\s+(?=[A-Z0-9"“'(])/g, m;
  while((m=re.exec(text))){ var end=m.index+m[0].length; res.push({s:start,e:end}); start=end; }
  if(start<text.length) res.push({s:start,e:text.length});
  return res;
}
function nearestBlock(node){
  var el=node&&node.nodeType===3?node.parentNode:node;
  while(el&&el!==content){
    if(/^(P|LI|TD|TH|BLOCKQUOTE|H1|H2|H3|H4|H5|H6|DIV|DD|DT|FIGCAPTION)$/.test(el.tagName)) return el;
    el=el.parentNode;
  }
  return null;
}
// Expand a selection to the sentence(s) it covers PLUS one sentence on each side,
// so partial selections and abbreviations like ".NET" still translate as full context.
function expandSelection(sel, fallback){
  try{
    if(!sel||!sel.anchorNode) return fallback;
    var block=nearestBlock(sel.anchorNode);
    var selText=sel.toString();
    var text=block?block.textContent:"";
    if(!text) return selText||fallback;
    var idx=text.indexOf(selText);
    if(idx<0) return selText||fallback;
    var start=idx, end=idx+selText.length;
    var sents=splitSentencesSmart(text), first=-1, last=-1, i;
    for(i=0;i<sents.length;i++){ if(sents[i].e>start && sents[i].s<end){ if(first<0)first=i; last=i; } }
    if(first<0) return selText||fallback;
    var lo=Math.max(0,first-1), hi=Math.min(sents.length-1,last+1);
    var withN=text.slice(sents[lo].s, sents[hi].e).trim();
    if(withN.length<=480) return withN;                    // MyMemory free-tier length guard
    var matched=text.slice(sents[first].s, sents[last].e).trim();
    if(matched.length<=480) return matched;
    return (selText||fallback).slice(0,480);
  }catch(e){ return fallback; }
}
// Scroll to and briefly highlight the first occurrence of q in the current chapter.
function caretAt(x,y){
  if(document.caretRangeFromPoint) return document.caretRangeFromPoint(x,y);
  if(document.caretPositionFromPoint){ var pp=document.caretPositionFromPoint(x,y);
    if(pp){ var r=document.createRange(); r.setStart(pp.offsetNode,pp.offset); return r; } }
  return null;
}
function textOffsetInBlock(block, container, offsetInNode){
  var w=document.createTreeWalker(block, NodeFilter.SHOW_TEXT, null), n, total=0;
  while((n=w.nextNode())){ if(n===container) return total+offsetInNode; total+=n.nodeValue.length; }
  return total;
}
// The sentence at `off`, plus the previous and next sentence.
function wordAtOffset(text, off){
  if(off<0) off=0; if(off>=text.length) off=text.length-1;
  if(off<0) return "";
  var ws=function(c){ return c===undefined || /\s/.test(c); };
  if(ws(text[off])){ if(off>0 && !ws(text[off-1])) off=off-1; else return ""; }
  var l=off, r=off;
  while(l>0 && !ws(text[l-1])) l--;
  while(r<text.length && !ws(text[r])) r++;
  return text.slice(l,r).replace(/^[^\w.#]+/,"").replace(/[^\w#]+$/,"").trim();
}
function expandAtOffset(text, off){
  var sents=splitSentencesSmart(text), idx=-1, i;
  for(i=0;i<sents.length;i++){ if(off>=sents[i].s && off<sents[i].e){ idx=i; break; } }
  if(idx<0){ for(i=0;i<sents.length;i++){ if(off<sents[i].e){ idx=i; break; } } }
  if(idx<0) idx=sents.length-1;
  if(idx<0) return "";
  var lo=Math.max(0,idx-1), hi=Math.min(sents.length-1,idx+1);
  var out=text.slice(sents[lo].s, sents[hi].e).trim();
  if(out.length<=480) return out;
  return text.slice(sents[idx].s, sents[idx].e).trim().slice(0,480);
}
function highlightFind(q){
  q=(q||"").trim(); if(!q) return;
  var old=content.querySelector("mark.find-hit");
  if(old) old.replaceWith(document.createTextNode(old.textContent));
  var ql=q.toLowerCase();
  var walker=document.createTreeWalker(content,NodeFilter.SHOW_TEXT,null), node;
  while((node=walker.nextNode())){
    var par=node.parentNode; if(!par) continue;
    if(par.closest && par.closest("pre,code,.tr-popup")) continue;
    var idx=node.nodeValue.toLowerCase().indexOf(ql);
    if(idx>=0){
      try{
        var range=document.createRange();
        range.setStart(node,idx); range.setEnd(node,idx+q.length);
        var mark=document.createElement("mark"); mark.className="find-hit";
        range.surroundContents(mark);
        mark.scrollIntoView({block:"center",behavior:"smooth"});
        setTimeout(function(){ if(mark.parentNode) mark.replaceWith(document.createTextNode(mark.textContent)); },4000);
      }catch(e){}
      return;
    }
  }
}

function doTranslate(text, rect){
  if(!text || !rect) return;
  lastRect=rect;
  showPopup("…", rect, true);
  translate(text).then(function(uk){ showPopup(uk, rect, false); })
    .catch(function(err){ removePopup(); toast(err.message||"Translation failed. Check your connection."); });
}
// When Easy Translate is on: a selection or a word tap translates immediately.
document.addEventListener("mouseup",function(e){
  if(e.target.closest && e.target.closest(".tr-popup")) return;
  setTimeout(function(){
    if(!settings.easy) return;
    var sel=window.getSelection();
    var txt=sel&&sel.toString().trim();
    if(txt && txt.length>0 && sel.anchorNode && content.contains(sel.anchorNode)){
      doTranslate(txt, rectFromSelection());
      return;
    }
    if(content.contains(e.target) && !(e.target.closest && e.target.closest("a, button, pre, code, .codeblock, .tr-popup"))){
      var block=nearestBlock(e.target.nodeType===3?e.target.parentNode:e.target);
      var rng=caretAt(e.clientX, e.clientY);
      if(block && rng && rng.startContainer && block.contains(rng.startContainer)){
        var off=textOffsetInBlock(block, rng.startContainer, rng.startOffset);
        var word=wordAtOffset(block.textContent, off);
        if(word) doTranslate(word, {left:e.clientX, top:e.clientY, width:0, height:0, bottom:e.clientY});
      }
    }
  },10);
});
selBtn.addEventListener("mousedown",function(e){ e.preventDefault(); }); // preserve the selection
selBtn.addEventListener("click",function(){
  var r=rectFromSelection(); if(r) lastRect=r;
  selBtn.hidden=true;
  var textToTranslate=pendingText;
  var anchor=lastRect;
  showPopup("…", anchor, true);
  translate(textToTranslate).then(function(uk){ showPopup(uk, anchor, false); })
    .catch(function(err){ removePopup(); toast(err.message||"Translation failed. Check your connection."); });
});
function showPopup(text, r, loading){
  removePopup();
  if(!r) r=lastRect; if(!r) return;
  popup=document.createElement("div");
  popup.className="tr-popup"+(loading?" loading":"");
  popup.innerHTML='<span class="tr-lbl">🇺🇦 '+esc(settings.lang.toUpperCase())+'</span>'+
    '<span class="tr-txt">'+esc(text)+'</span><span class="tr-x" title="close">✕</span>';
  document.body.appendChild(popup);
  var cx=r.left+r.width/2+window.scrollX;
  var above=r.top>150;
  popup.style.left=cx+"px";
  if(above){ popup.style.top=(r.top+window.scrollY-8)+"px"; }
  else{ popup.classList.add("below"); popup.style.top=(r.bottom+window.scrollY+8)+"px"; }
  // keep within horizontal viewport
  var pr=popup.getBoundingClientRect();
  if(pr.left<8) popup.style.left=(cx+(8-pr.left))+"px";
  if(pr.right>window.innerWidth-8) popup.style.left=(cx-(pr.right-(window.innerWidth-8)))+"px";
  popup.querySelector(".tr-x").addEventListener("click",removePopup);
}
document.addEventListener("mousedown",function(e){
  if(popup && !e.target.closest(".tr-popup") && !e.target.closest(".sel-translate")) removePopup();
});
// Word taps and text selections are handled by the mouseup listener above: both
// show the Translate button, and the API is only called when that button is pressed.

/* ---------------- Theme ---------------- */
var themeBtn=document.getElementById("themeBtn");
var savedTheme=localStorage.getItem("theme")||"auto";
document.documentElement.setAttribute("data-theme",savedTheme);
themeBtn.addEventListener("click",function(){
  var cur=document.documentElement.getAttribute("data-theme");
  var next=cur==="dark"?"light":cur==="light"?"auto":"dark";
  document.documentElement.setAttribute("data-theme",next);
  localStorage.setItem("theme",next);
  toast("Theme: "+next);
});

/* ---------------- Language modal ---------------- */
var langModal=document.getElementById("langModal");
var emailInput=document.getElementById("emailInput");
var targetLang=document.getElementById("targetLang");
document.getElementById("langBtn").addEventListener("click",function(){
  emailInput.value=settings.email; targetLang.value=settings.lang; langModal.hidden=false;
});
document.getElementById("langClose").addEventListener("click",function(){
  settings.email=emailInput.value.trim(); settings.lang=targetLang.value;
  localStorage.setItem("tr_email",settings.email); localStorage.setItem("tr_lang",settings.lang);
  langModal.hidden=true;
});
document.getElementById("clearCache").addEventListener("click",function(){
  var n=0; Object.keys(localStorage).forEach(function(k){if(k.indexOf("tr:")===0){localStorage.removeItem(k);n++;}});
  toast("Cleared "+n+" cached translations");
});
langModal.addEventListener("click",function(e){if(e.target===langModal)langModal.hidden=true;});

/* ---------------- Easy Translate toggle ---------------- */
var easyToggle=document.getElementById("easyToggle");
easyToggle.checked=settings.easy;
easyToggle.addEventListener("change",function(){
  settings.easy=easyToggle.checked;
  localStorage.setItem("tr_easy", settings.easy?"1":"0");
  if(!settings.easy){ selBtn.hidden=true; removePopup(); }
  toast(settings.easy?"Easy Translate: on — tap a word or select text":"Easy Translate: off");
});

/* ---------------- Sidebar (mobile) ---------------- */
var sidebar=document.getElementById("sidebar"), scrim=document.getElementById("scrim");
document.getElementById("menuBtn").addEventListener("click",function(){
  sidebar.classList.toggle("open"); scrim.classList.toggle("show");
});
function closeSidebar(){sidebar.classList.remove("open");scrim.classList.remove("show");}
scrim.addEventListener("click",closeSidebar);
document.getElementById("brandHome").addEventListener("click",function(){navigate(BOOK[0].slug);});

/* ---------------- Reading progress + scrollspy ---------------- */
var progress=document.getElementById("reading-progress");
window.addEventListener("scroll",function(){
  var h=document.documentElement;
  var max=h.scrollHeight-h.clientHeight;
  var p=max>0?(h.scrollTop/max*100):0;
  progress.style.setProperty("--p",p.toFixed(1)+"%");
  scheduleSave();
  var links=outlineEl.querySelectorAll("a"); var activeId=null;
  content.querySelectorAll("h2,h3").forEach(function(hd){
    if(hd.getBoundingClientRect().top<120) activeId=hd.id;
  });
  links.forEach(function(a){a.classList.toggle("active",a.dataset.id===activeId);});
  var _act=outlineEl.querySelector("a.active");
  if(_act){ var _oT=outlineEl.scrollTop,_oH=outlineEl.clientHeight,_aT=_act.offsetTop,_aH=_act.offsetHeight;
    if(_aT<_oT) outlineEl.scrollTop=_aT-8;
    else if(_aT+_aH>_oT+_oH) outlineEl.scrollTop=_aT+_aH-_oH+8; }
},{passive:true});

/* ---------------- Toast ---------------- */
var toastEl=document.getElementById("toast"), toastT;
function toast(msg,ms){
  toastEl.textContent=msg; toastEl.hidden=false;
  clearTimeout(toastT); toastT=setTimeout(function(){toastEl.hidden=true;},ms||2600);
}

/* ---------------- What's New: release popup + read-tracked chapter links ----------------
   The 101-whats-new chapter is the changelog. Its latest "## Release — <date>" section is
   shown in a popup once per release (localStorage "wn_seen"). Chapter links inside release
   sections get a per-user read mark (localStorage "wn_read") once clicked. */
var WN=null;
BOOK.forEach(function(c){ if((c.id||"").indexOf("whats-new")>=0) WN=c; });

function wnHash(s){ var h=5381,i; for(i=0;i<s.length;i++){ h=((h<<5)+h+s.charCodeAt(i))|0; } return (h>>>0).toString(36); }
function wnLatest(){
  if(!WN) return null;
  var m=WN.md.match(/^##\s+Release[^\n]*$/m);
  if(!m) return null;
  var rest=WN.md.slice(WN.md.indexOf(m[0])+m[0].length);
  var next=rest.search(/^##\s+/m);
  var heading=m[0].replace(/^##\s+/,"").trim();
  var md=rest.slice(0,next<0?rest.length:next).trim();
  // seenKey includes a content hash so an amended release re-triggers the popup.
  return { heading:heading, md:md, seenKey:heading+"|"+wnHash(md),
           date:m[0].replace(/^##\s+Release\s*[—–-]*\s*/,"").trim() };
}
function wnRead(){ try{ return JSON.parse(localStorage.getItem("wn_read")||"{}"); }catch(e){ return {}; } }
function wnMarkSeen(){ var l=wnLatest(); if(l){ try{ localStorage.setItem("wn_seen",l.seenKey); }catch(e){} } }
// Decorate chapter links inside release sections: unread = accent dot, read = ✓.
// releaseId is passed for popup content; in the full chapter it is tracked per H2.
// Keys include the link's position so several links to one chapter track separately.
function wnDecorate(root, releaseId){
  var read=wnRead(), rel=releaseId||null, n=0;
  root.querySelectorAll("h2, a[href^='#']").forEach(function(el){
    if(el.tagName==="H2"){ rel=/^Release/.test(el.textContent)?el.textContent.trim():null; n=0; return; }
    if(!rel) return;
    var slug=el.getAttribute("href").slice(1), sec=null;
    if(!bySlug[slug]){
      // A section link: resolve it to its chapter, and remember to scroll there.
      var owner=headingOwner[slug];
      if(!owner) return;
      sec=slug; slug=owner;
    }
    // The key stays keyed on the chapter, so ✓ marks on already-published releases survive.
    var key=rel+"|"+slug+"|"+(n++);
    el.classList.add("wn-link");
    if(read[key]) el.classList.add("wn-read");
    el.addEventListener("click",function(ev){
      ev.preventDefault();
      var r=wnRead(); r[key]=1;
      try{ localStorage.setItem("wn_read",JSON.stringify(r)); }catch(e){}
      el.classList.add("wn-read");
      wnHide();
      navigate(slug, sec);
    });
  });
}
var wnModal=document.getElementById("wnModal");
function wnHide(){ wnModal.hidden=true; }
function wnShow(){
  var latest=wnLatest(); if(!latest||!latest.md) return;
  document.getElementById("wnDate").textContent=latest.date;
  var saved=usedIds; usedIds={};
  document.getElementById("wnBody").innerHTML=render(latest.md);
  usedIds=saved;
  wnDecorate(document.getElementById("wnBody"), latest.heading);
  wnModal.hidden=false;
  wnMarkSeen();
}
function wnMaybeShow(){
  var latest=wnLatest(); if(!latest) return;
  if(localStorage.getItem("wn_seen")===latest.seenKey) return;
  if(current&&WN&&current.id===WN.id){ wnMarkSeen(); return; } // already on the page
  wnShow();
}
document.getElementById("wnClose").addEventListener("click",wnHide);
document.getElementById("wnOpen").addEventListener("click",function(){ wnHide(); if(WN) navigate(WN.slug); });
wnModal.addEventListener("click",function(e){ if(e.target===wnModal) wnHide(); });

/* ---------------- Boot ---------------- */
function currentSlug(){var m=location.hash.match(/^#\/(.+)$/);return m?m[1]:null;}
window.addEventListener("hashchange",function(){var s=currentSlug();if(s)go(s,false);});
window.addEventListener("beforeunload", saveProgress);
buildNav();
// A shared link's hash still wins; otherwise always start at the front of the book.
go(currentSlug()||BOOK[0].slug,false);
try{ localStorage.removeItem("last"); }catch(e){}   // no longer used
wnMaybeShow();
})();
