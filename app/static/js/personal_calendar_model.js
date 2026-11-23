/* Calendar projections are pure functions: no HTTP, storage, or mutation of server data. */
(() => {
  const DAY = 86400000, ZONE = 'Asia/Shanghai', formats = new Map();
  const labels = {interview:'面试',exam:'笔试',course:'课程',recruiting:'招聘活动',meeting:'普通会议',personal:'个人事项'};
  const esc = text => String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const shift = (day, n) => new Date(Date.parse(day+'T00:00:00Z')+n*DAY).toISOString().slice(0,10);
  const weekday = day => (new Date(day+'T00:00:00Z').getUTCDay()+6)%7;
  const hm = minutes => `${String(Math.floor(minutes/60)).padStart(2,'0')}:${String(minutes%60).padStart(2,'0')}`;
  function parts(ms, zone=ZONE) {
    if(!formats.has(zone)) formats.set(zone,new Intl.DateTimeFormat('en-CA',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hourCycle:'h23'}));
    const p=Object.fromEntries(formats.get(zone).formatToParts(ms).map(p=>[p.type,p.value]));
    return {day:`${p.year}-${p.month}-${p.day}`, minutes:Number(p.hour)*60+Number(p.minute)};
  }
  function wallMs(day, minutes, zone=ZONE) {
    if(zone===ZONE) return Date.parse(`${day}T${hm(minutes)}:00+08:00`);
    const target=Date.parse(day+'T00:00:00Z')+minutes*60000;
    const offsets=new Set([-36,0,36].map(h=>{const ms=target+h*3600000,p=parts(ms,zone);return Date.parse(p.day+'T00:00:00Z')+p.minutes*60000-ms}));
    const candidates=[...offsets].map(offset=>target-offset);
    const exact=candidates.filter(ms=>{const p=parts(ms,zone);return p.day===day&&p.minutes===minutes});
    // ZoneInfo's fold=0 chooses the earlier fall-back occurrence and pre-gap offset.
    return exact.length?Math.min(...exact):Math.max(...candidates);
  }
  function scheduledCount(first,last,days) {
    if(last<first)return 0;
    const count=Math.round((Date.parse(last+'T00:00:00Z')-Date.parse(first+'T00:00:00Z'))/DAY)+1;
    let total=Math.floor(count/7)*days.length;
    for(let i=0;i<count%7;i++)if(days.includes((weekday(first)+i)%7))total++;
    return total;
  }
  function repeatStats(event, day, now) {
    const zone=event.timezone||ZONE,start=parts(Date.parse(event.start_at),zone),end=parts(Date.parse(event.end_at),zone),today=parts(now,zone).day;
    const until=day<today?day:today, completions=event.repeat_completions||{};
    const pastToday=wallMs(today,end.minutes,zone)<now;
    const past=until===today&&!pastToday?shift(until,-1):until;
    const count=scheduledCount(start.day,past,event.repeat_weekdays);
    const completed=Object.keys(completions).filter(d=>d>=start.day&&d<=past&&event.repeat_weekdays.includes(weekday(d))).length;
    let streak=0,cursor=until;
    if(cursor===today&&!pastToday&&!completions[cursor])cursor=shift(cursor,-1);
    while(cursor>=start.day) {
      if(event.repeat_weekdays.includes(weekday(cursor))){if(!completions[cursor])break;streak++;}
      cursor=shift(cursor,-1);
    }
    return {repeat_streak:streak,repeat_missed:Math.max(0,count-completed)};
  }
  function datesFor(selected,view) {
    if(view==='day')return [selected];
    const first=view==='week'?shift(selected,-weekday(selected)):selected.slice(0,7)+'-01';
    const start=view==='week'?first:shift(first,-weekday(first));
    return Array.from({length:view==='week'?7:42},(_,i)=>shift(start,i));
  }
  function project(snapshot, selected, view='day', now=Date.now()) {
    const dates=datesFor(selected,view),today=parts(now).day, needed=new Set();
    for(const d of [...dates,...Array.from({length:8},(_,i)=>shift(today,i))]) for(let n=-2;n<=2;n++)needed.add(shift(d,n));
    const expanded=[];
    for(const source of snapshot.events) {
      if(!source.repeat_weekdays?.length){expanded.push({...source,start:Date.parse(source.start_at),end:Date.parse(source.end_at)});continue;}
      const zone=source.timezone||ZONE,a=parts(Date.parse(source.start_at),zone),b=parts(Date.parse(source.end_at),zone);
      for(const day of needed) {
        if(day<a.day||source.repeat_closed_on&&day>=source.repeat_closed_on||!source.repeat_weekdays.includes(weekday(day)))continue;
        expanded.push({...source,id:`${source.id}~${day}`,start:wallMs(day,a.minutes,zone),end:wallMs(day,b.minutes,zone),
          completed_at:source.repeat_completions?.[day]||null,...repeatStats(source,day,now)});
      }
    }
    expanded.sort((a,b)=>a.start-b.start||a.id.localeCompare(b.id));
    function segment(event,day) {
      const start=wallMs(day,0),end=start+DAY;
      if(event.skip_weekends&&weekday(day)>=5||event.start>=end||event.end<=start)return null;
      let a=Math.max(start,event.start),b=Math.min(end,event.end);
      if(event.workday_only){a=Math.max(a,wallMs(day,Number(snapshot.workday_start.slice(0,2))*60+Number(snapshot.workday_start.slice(3))));b=Math.min(b,wallMs(day,Number(snapshot.workday_end.slice(0,2))*60+Number(snapshot.workday_end.slice(3))));}
      if(a>=b)return null;
      return {...event,startMin:(a-start)/60000,endMin:(b-start)/60000,before:event.start<start,after:event.end>end};
    }
    return {dates,today,expanded,segment,now,selected,view};
  }
  const classes=e=>`type-${esc(e.type)}${e.repeat_weekdays?.length?' is-repeating':''}${e.completed_at?' is-completed':''}${e.cancelled_at?' is-cancelled':''}`;
  const url=e=>`/my-schedule/events/${encodeURIComponent(e.id)}`;
  const nav=(view,day,more=false)=>`/my-schedule?view=${view}&amp;date_value=${day}${more?'&amp;show_all=1':''}`;
  const title=e=>esc(e.title)+(e.cancelled_at?' · 已废除':'');
  function summaries(ctx,days) {
    const items=ctx.expanded.filter(e=>parts(e.start).day!==parts(e.end-1).day).map(e=>({e,indexes:days.map((d,i)=>ctx.segment(e,d)?i:-1).filter(i=>i>=0)})).filter(x=>x.indexes.length);
    if(!items.length)return '';
    return `<div class="calendar-cross-summary" style="--summary-cols:${days.length}"><span class="calendar-cross-label">跨天</span>`+items.map(({e,indexes},i)=>`<a class="calendar-cross-item ${classes(e)}" style="grid-column:${Math.min(...indexes)+1}/${Math.max(...indexes)+2};grid-row:${i+1}" href="${url(e)}">${title(e)} · ${parts(e.start).day.slice(5)}—${parts(e.end).day.slice(5)}</a>`).join('')+'</div>';
  }
  function dayHTML(ctx) {
    const items=ctx.expanded.map(e=>ctx.segment(e,ctx.selected)).filter(Boolean);
    const first=Math.min(6,...items.map(e=>Math.floor(e.startMin/60))),last=Math.max(23,...items.map(e=>Math.ceil(e.endMin/60)));
    const count=items.length,p=parts(ctx.now),heading=`${ctx.selected} 星期${'一二三四五六日'[weekday(ctx.selected)]} · ${count} 项安排`;
    return `<div class="calendar-day-heading"><h2>${heading}</h2></div>`+summaries(ctx,[ctx.selected])+`<div class="time-table personal-time-table"><div class="time-grid-wrap" style="--hour-height:88px;--start-hour:${first}"><div class="time-labels">`+
      Array.from({length:last-first+1},(_,i)=>`<span class="time-label" style="top:${i*88}px">${hm((first+i)*60)}</span>`).join('')+
      `</div><div class="time-grid" data-now-grid data-calendar-date="${ctx.selected}" data-time-zone="${ZONE}" data-start-minute="${first*60}" data-end-minute="${last*60}" style="height:${(last-first)*88}px"><div class="now-line" data-now-line ${ctx.selected!==ctx.today?'hidden':''} style="top:${(p.minutes/60-first)*88}px"><span class="now-line-label">当前时间 ${hm(p.minutes)}</span></div>`+
      items.map(e=>`<a class="event-card ${classes(e)}" href="${url(e)}" style="--start-min:${e.startMin};--duration:${e.endMin-e.startMin};--column:1;--columns:1"><div class="personal-event-content"><div class="event-head"><span class="event-title">${title(e)}</span><span class="event-time">${hm(e.startMin)}—${hm(e.endMin)}</span></div><div class="event-meta">${labels[e.type]}${e.before?' · 从前一天延续':''}${e.after?' · 延续到次日':''}${e.completed_at?' · 已完成':''}</div></div></a>`).join('')+
      '</div></div></div>'+(!count?'<div class="empty-state"><div class="empty-title">这一天还没有安排</div></div>':'');
  }
  function weekHTML(ctx) {
    const lists=ctx.dates.map(day=>ctx.expanded.map(e=>ctx.segment(e,day)).filter(Boolean)),keys=[];
    for(const e of lists.flat().sort((a,b)=>Number(parts(a.start).day===parts(a.end-1).day)-Number(parts(b.start).day===parts(b.end-1).day)||a.start-b.start))if(!keys.includes(e.id.split('~')[0]))keys.push(e.id.split('~')[0]);
    return `<div class="calendar-day-heading"><h2>${ctx.dates[0]} — ${ctx.dates.at(-1).slice(5)} · ${lists.flat().filter((e,i,a)=>a.findIndex(x=>x.id===e.id)===i).length} 项安排</h2></div><div class="calendar-week-board" aria-label="周日程，按任务对齐"><div class="calendar-board-heading">`+ctx.dates.map((d,i)=>`<a href="${nav('day',d)}" class="${d===ctx.today?'is-today':''}"><span>周${'一二三四五六日'[i]}</span><strong>${Number(d.slice(8))}</strong><small>${lists[i].length} 项安排</small></a>`).join('')+'</div>'+summaries(ctx,ctx.dates)+
      `<div class="calendar-board-columns" style="--board-rows:${Math.max(1,keys.length)}">`+ctx.dates.map((d,i)=>`<div class="calendar-board-day ${d===ctx.today?'is-today':''}">`+lists[i].map(e=>{const key=e.id.split('~')[0],row=keys.indexOf(key)+1;return `<a class="calendar-board-event ${classes(e)}" href="${url(e)}" data-board-key="${esc(key)}" data-board-row="${row}" style="grid-row:${row}"><span class="calendar-board-label">${e.repeat_weekdays?.length?'重复任务':e.before||e.after?'跨天 · 当日安排':labels[e.type]}</span><strong>${title(e)}</strong><small>${hm(e.startMin)}—${hm(e.endMin)}</small>${e.before&&ctx.segment(e,shift(d,-1))?'<span class="calendar-task-arrow from-left" aria-label="从前一天延续">←</span>':''}${e.after&&ctx.segment(e,shift(d,1))?'<span class="calendar-task-arrow to-right" aria-label="延续到次日">→</span>':''}</a>`}).join('')+'</div>').join('')+'</div></div>';
  }
  function monthHTML(ctx) {
    let html=`<div class="calendar-day-heading"><h2>${ctx.selected.slice(0,4)} 年 ${Number(ctx.selected.slice(5,7))} 月</h2></div><div class="personal-period-calendar personal-period-month"><div class="personal-period-weekdays">${[...'一二三四五六日'].map(d=>`<span>周${d}</span>`).join('')}</div><div class="personal-period-grid">`;
    for(let offset=0;offset<42;offset+=7){const days=ctx.dates.slice(offset,offset+7),lists=days.map(d=>ctx.expanded.map(e=>ctx.segment(e,d)).filter(Boolean)),keys=[];
      for(const e of lists.flat())if(!keys.includes(e.id.split('~')[0]))keys.push(e.id.split('~')[0]);
      html+=days.map((d,i)=>`<div class="personal-period-day ${d.slice(0,7)!==ctx.selected.slice(0,7)?'outside-month':''} ${d===ctx.today?'is-today':''}${d===ctx.selected?' is-selected':''}"><a class="personal-period-date" href="${nav('day',d)}" aria-label="查看 ${d} 的日程">${d.slice(0,7)!==ctx.selected.slice(0,7)?d.slice(5).replace('-','/'):Number(d.slice(8))}</a><div class="personal-period-events">`+lists[i].map(e=>`<a class="personal-period-event ${classes(e)}" style="grid-row:${keys.indexOf(e.id.split('~')[0])+1}" href="${url(e)}" title="${esc(e.title)}">${e.before?'← ':''}${e.completed_at?'✓ ':''}${title(e)}${e.after?' →':''}</a>`).join('')+'</div></div>').join('');
    }return html+'</div></div>';
  }
  const csrfField=csrf=>csrf?`<input type="hidden" name="csrf_token" value="${esc(csrf)}">`:'';
  function sideHTML(ctx,csrf,more) {
    const todayStart=wallMs(ctx.today,0),tomorrow=todayStart+DAY;
    const remaining=ctx.expanded.filter(e=>!e.completed_at&&!e.cancelled_at&&(e.end>ctx.now||e.repeat_weekdays?.length&&parts(e.start).day===ctx.today));
    const upcoming=more?remaining:remaining.filter(e=>e.start<tomorrow&&!(e.skip_weekends&&weekday(ctx.today)>=5));
    const completed=ctx.expanded.filter(e=>e.completed_at&&parts(Date.parse(e.completed_at)).day===ctx.today).sort((a,b)=>Date.parse(b.completed_at)-Date.parse(a.completed_at));
    const overdue=ctx.expanded.filter(e=>!e.completed_at&&!e.cancelled_at&&!e.repeat_weekdays?.length&&e.end<=ctx.now).sort((a,b)=>a.end-b.end);
    const stats=e=>e.repeat_weekdays?.length?`<small class="personal-repeat-stats"> · 连续完成 ${e.repeat_streak||0} 天 · 累积未完成 ${e.repeat_missed||0} 天</small>`:'';
    const card=e=>`<a href="${url(e)}" class="personal-upcoming-item"><span>${parts(e.start).day.slice(5)} ${hm(parts(e.start).minutes)} · ${labels[e.type]}</span><strong>${title(e)}${stats(e)}</strong></a>`;
    const completion=(e,done)=>`<form action="${url(e)}/completion" method="post">${csrfField(csrf)}<input type="hidden" name="completed" value="${done}"><button class="personal-side-action" type="submit">${done?'完成':'恢复'}</button></form>`;
    return `<section class="personal-upcoming-section"><div class="personal-side-heading"><h2>接下来的安排</h2>${more||remaining.length>upcoming.length?`<a href="${nav(ctx.view,ctx.selected,!more)}">${more?'收起':'显示更多'}</a>`:''}</div><div class="personal-side-list">`+upcoming.map(e=>`<div class="personal-side-row">${card(e)}${completion(e,true)}</div>`).join('')+(!upcoming.length?'<p class="muted-text">今天暂无待完成安排</p>':'')+'</div></section>'+
      (overdue.length?`<section class="personal-overdue-section"><div class="personal-side-heading"><h2>未完成日程 ${overdue.length}</h2></div><div class="personal-side-list">`+overdue.map(e=>{const min=parts(Math.max(ctx.now+60000,e.end)),id='defer-'+e.id;return `<div class="personal-overdue-row">${card(e)}<div class="personal-overdue-actions"><button type="button" class="personal-side-action" data-defer-open="${esc(id)}">延期</button>${completion(e,true)}<form action="${url(e)}/overdue" method="post">${csrfField(csrf)}<input type="hidden" name="action" value="cancel"><button type="submit" class="personal-side-action">废除</button></form></div><dialog id="${esc(id)}" class="personal-defer-dialog"><h2>延期：${title(e)}</h2><p>新的结束时间不可早于原结束时间或当前时间。</p><form action="${url(e)}/overdue" method="post" data-defer-form>${csrfField(csrf)}<input type="hidden" name="action" value="defer"><label>新的结束时间<input class="input" type="datetime-local" name="end_at" required min="${min.day}T${hm(min.minutes)}"></label><p role="alert" data-defer-error></p><div class="settings-actions"><button type="button" class="btn secondary" data-defer-close>取消</button><button type="submit" class="btn">确认延期</button></div></form></dialog></div>`}).join('')+'</div></section>':'')+
      `<details class="personal-completed-section" ${completed.length?'open':''}><summary>今日已完成 ${completed.length||''}</summary><div class="personal-side-list">`+completed.map(e=>`<div class="personal-side-row is-completed">${card(e)}${completion(e,false)}</div>`).join('')+(!completed.length?'<p class="muted-text">今天还没有完成的安排</p>':'')+'</div></details><a class="btn secondary personal-workspace-link" href="/my-schedule/interviews">打开我的面试工作台</a>';
  }
  const api={parts,wallMs,shift,weekday,datesFor,scheduledCount,repeatStats,project,dayHTML,weekHTML,monthHTML,sideHTML,esc};
  if(typeof module!=='undefined'&&module.exports)module.exports=api;else window.awesomeInterviewCalendarModel=api;
})();
