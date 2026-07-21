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
      var base=(txt.toLowerCase().replace(/[^\w\s-]/g,"").replace(/[\s_]+/g,"-").replace(/^-+|-+$/g,""))||"section";
      usedIds[base]=(usedIds[base]||0)+1;
      var slug=usedIds[base]===1?base:base+"-"+usedIds[base];
      html.push("<h"+lvl+' id="'+slug+'">'+inline(txt)+"</h"+lvl+">");
      i++; continue;
    }
    if(/^(-{3,}|\*{3,}|_{3,})\s*$/.test(line)){ html.push("<hr>"); i++; continue; }
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
          !/^(-{3,}|\*{3,})\s*$/.test(lines[i])){
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
var settings={ email: localStorage.getItem("tr_email")||"", lang: localStorage.getItem("tr_lang")||"uk" };
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
var pendingFind=null;
var _saveT;
function saveLast(){ if(!current) return; try{
  localStorage.setItem("last", current.slug);
  localStorage.setItem("pos:"+current.slug, String(Math.round(window.pageYOffset||document.documentElement.scrollTop||0)));
}catch(e){} }
function scheduleSave(){ clearTimeout(_saveT); _saveT=setTimeout(saveLast,400); }

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
      '</span>'+esc(c.nav)+(rt?'<span class="rt">'+rt+'</span>':'')+'</a>';
  });
  navEl.innerHTML=html;
}
function go(slug, push, restore){
  saveLast();
  var c=bySlug[slug]||BOOK[0];
  current=c;
  removePopup();
  usedIds={};
  content.innerHTML=render(c.md);
  postProcess(c);
  buildOutline(c);
  buildPager(c);
  highlightNav(c.slug);
  if(restore){ var _y=parseInt(localStorage.getItem("pos:"+c.slug)||"0",10)||0; window.scrollTo(0,_y); }
  else window.scrollTo(0,0);
  try{ localStorage.setItem("last", c.slug); }catch(e){}
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
      var a=document.createElement("a"); a.className="home-card"; a.href="#/"+ch.slug;
      a.innerHTML='<div class="hc-num">'+esc(numMatch?numMatch[1]:"")+'</div>'+
        '<div class="hc-ttl">'+esc(ch.nav)+'</div>'+(rt?'<div class="hc-rt">⏱️ '+rt+'</div>':'');
      grid.appendChild(a);
    });
    var h=document.createElement("h2"); h.textContent="Browse chapters";
    content.appendChild(h); content.appendChild(grid);
  }
  content.querySelectorAll('a[href^="#"]').forEach(function(a){
    var href=a.getAttribute("href");
    if(href.indexOf("#/")===0) return;
    var slug=href.slice(1);
    a.addEventListener("click",function(ev){
      if(bySlug[slug]){ ev.preventDefault(); location.hash="#/"+slug; }
      else{ var target=document.getElementById(slug); if(target){ ev.preventDefault(); target.scrollIntoView(); } }
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
var lastSel="", lastRect=null, popup=null;
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

document.addEventListener("mouseup",function(e){
  if(e.target.closest && e.target.closest(".tr-popup,.sel-translate")) return;
  setTimeout(function(){
    var sel=window.getSelection();
    var txt=sel&&sel.toString().trim();
    if(txt && txt.length>1 && sel.anchorNode && content.contains(sel.anchorNode)){
      lastSel=txt; lastRect=rectFromSelection();
      if(lastRect){
        selBtn.style.left=(lastRect.left+lastRect.width/2+window.scrollX)+"px";
        selBtn.style.top=(lastRect.top+window.scrollY)+"px";
        selBtn.hidden=false;
      }
    }else{ selBtn.hidden=true; }
  },10);
});
selBtn.addEventListener("mousedown",function(e){ e.preventDefault(); }); // preserve the selection
selBtn.addEventListener("click",function(){
  var r=rectFromSelection(); if(r) lastRect=r;
  selBtn.hidden=true;
  var textToTranslate=expandSelection(window.getSelection(), lastSel);
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
// Click a word in the text to translate its sentence plus the previous and next one.
content.addEventListener("click",function(e){
  if(e.target.closest("a, button, pre, code, .codeblock, .tr-popup, .sel-translate")) return;
  var sel=window.getSelection();
  if(sel && sel.toString().trim().length>0) return;      // a drag-selection is handled by the button
  var start=e.target.nodeType===3?e.target.parentNode:e.target;
  var block=nearestBlock(start);
  if(!block) return;
  var rng=caretAt(e.clientX, e.clientY);
  if(!rng || !rng.startContainer || !block.contains(rng.startContainer)) return;
  var off=textOffsetInBlock(block, rng.startContainer, rng.startOffset);
  var textToTranslate=expandAtOffset(block.textContent, off);
  if(!textToTranslate) return;
  var rect={left:e.clientX, top:e.clientY, width:0, height:0, bottom:e.clientY};
  lastRect=rect;
  showPopup("…", rect, true);
  translate(textToTranslate).then(function(uk){ showPopup(uk, rect, false); })
    .catch(function(err){ removePopup(); toast(err.message||"Translation failed. Check your connection."); });
});

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

/* ---------------- Sidebar (mobile) ---------------- */
var sidebar=document.getElementById("sidebar"), scrim=document.getElementById("scrim");
document.getElementById("menuBtn").addEventListener("click",function(){
  sidebar.classList.toggle("open"); scrim.classList.toggle("show");
});
function closeSidebar(){sidebar.classList.remove("open");scrim.classList.remove("show");}
scrim.addEventListener("click",closeSidebar);
document.getElementById("brandHome").addEventListener("click",function(){location.hash="#/"+BOOK[0].slug;});

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

/* ---------------- Boot ---------------- */
function currentSlug(){var m=location.hash.match(/^#\/(.+)$/);return m?m[1]:null;}
window.addEventListener("hashchange",function(){var s=currentSlug();if(s)go(s,false);});
window.addEventListener("beforeunload", saveLast);
buildNav();
go(currentSlug()||localStorage.getItem("last")||BOOK[0].slug,false,true);
})();
