const pptxgen = require('pptxgenjs');

const pptx = new pptxgen();
pptx.layout = 'LAYOUT_WIDE';
pptx.author = 'OpenAI coding assistant';
pptx.subject = 'DP.WorkFlow C# 流程图架构与逻辑梳理';
pptx.title = 'DP.WorkFlow 流程图架构梳理';
pptx.company = 'DP.WorkFlow';
pptx.lang = 'zh-CN';
pptx.theme = {
  headFontFace: 'Microsoft YaHei',
  bodyFontFace: 'Microsoft YaHei',
  lang: 'zh-CN'
};
pptx.defineSlideMaster({
  title: 'CONTENT',
  background: { color: 'F4F7F7' },
  objects: [
    { text: { text: 'WORKFLOW.REBUILD  ·  ARCHITECTURE REVIEW', options: { x: 0.58, y: 0.18, w: 5.4, h: 0.2, fontFace: 'Arial', fontSize: 8, bold: true, color: '4E6867', charSpacing: 1.3, margin: 0 } } },
    { text: { text: '2025', options: { x: 12.2, y: 7.12, w: 0.55, h: 0.16, fontFace: 'Arial', fontSize: 8, color: '718583', align: 'right', margin: 0 } } }
  ],
  slideNumber: { x: 12.82, y: 7.1, w: 0.2, h: 0.18, fontFace: 'Arial', fontSize: 8, color: '718583', margin: 0 }
});

const C = {
  ink: '173231', ink2: '294A48', teal: '0C7C79', mint: '32C3A5', lime: 'B7E34A', orange: 'F18F3B',
  red: 'D95D5D', paper: 'F4F7F7', white: 'FFFFFF', pale: 'E6F0EF', pale2: 'D5E7E5', gray: '718583',
  line: 'AFC5C2', darkBg: '102A29', code: '213D3B'
};
const FONT = 'Microsoft YaHei';
const SH = pptx.ShapeType;

function addTitle(slide, title, subtitle) {
  slide.addText(title, { x: 0.58, y: 0.55, w: 11.9, h: 0.55, fontFace: FONT, fontSize: 26, bold: true, color: C.ink, margin: 0, breakLine: false });
  if (subtitle) slide.addText(subtitle, { x: 0.6, y: 1.14, w: 11.7, h: 0.3, fontFace: FONT, fontSize: 11, color: C.gray, margin: 0 });
}
function addText(slide, text, x, y, w, h, size=15, color=C.ink, opts={}) {
  slide.addText(text, { x, y, w, h, fontFace: opts.fontFace || FONT, fontSize: size, color, margin: opts.margin ?? 0.06,
    bold: opts.bold || false, align: opts.align || 'left', valign: opts.valign || 'mid', breakLine: false,
    fit: 'shrink', ...opts });
}
function rr(slide, x, y, w, h, fill=C.white, line=C.line, radius=0.12, shadow=false) {
  slide.addShape(SH.roundRect, { x, y, w, h, rectRadius: radius, fill: { color: fill }, line: { color: line, width: 1 },
    shadow: shadow ? { type: 'outer', color: '5B7472', opacity: 0.16, blur: 1.5, angle: 45, distance: 1 } : undefined });
}
function pill(slide, text, x, y, w, fill=C.teal, color=C.white) {
  slide.addShape(SH.roundRect, { x, y, w, h: 0.3, rectRadius: 0.14, fill: { color: fill }, line: { color: fill } });
  addText(slide, text, x, y+0.01, w, 0.27, 9, color, { bold: true, align: 'center', margin: 0 });
}
function node(slide, x, y, w, h, title, sub, fill=C.white, accent=C.teal) {
  rr(slide, x, y, w, h, fill, fill === C.white ? C.line : fill, 0.12, true);
  slide.addShape(SH.ellipse, { x: x-0.055, y: y+h/2-0.055, w: 0.11, h: 0.11, fill: { color: accent }, line: { color: accent } });
  slide.addShape(SH.ellipse, { x: x+w-0.055, y: y+h/2-0.055, w: 0.11, h: 0.11, fill: { color: accent }, line: { color: accent } });
  addText(slide, title, x+0.18, y+0.14, w-0.36, 0.3, 15, C.ink, { bold: true, align: 'center', margin: 0 });
  if (sub) addText(slide, sub, x+0.16, y+0.5, w-0.32, h-0.57, 9.5, C.gray, { align: 'center', valign: 'top', margin: 0 });
}
function lineBetween(slide, x1, y1, x2, y2, color=C.teal, width=2, dash='solid', endArrowType='none', transparency=0) {
  slide.addShape(SH.line, {
    x: Math.min(x1, x2), y: Math.min(y1, y2), w: Math.abs(x2-x1), h: Math.abs(y2-y1),
    flipH: x2 < x1, flipV: y2 < y1,
    line: { color, width, transparency, beginArrowType: 'none', endArrowType, dashType: dash }
  });
}
function arrow(slide, x1, y1, x2, y2, color=C.teal, width=2, dash='solid') {
  lineBetween(slide, x1, y1, x2, y2, color, width, dash, 'triangle');
}
function card(slide, x, y, w, h, label, title, body, accent=C.teal) {
  rr(slide, x, y, w, h, C.white, 'D5E2E0', 0.12, true);
  pill(slide, label, x+0.22, y+0.2, Math.min(w-0.44, Math.max(0.7, label.length*0.17)), accent);
  addText(slide, title, x+0.22, y+0.65, w-0.44, 0.4, 18, C.ink, { bold: true, margin: 0 });
  addText(slide, body, x+0.22, y+1.14, w-0.44, h-1.35, 12.5, C.ink2, { valign: 'top', margin: 0, breakLine: false });
}
function bullets(slide, items, x, y, w, h, size=13, color=C.ink2) {
  const runs = [];
  items.forEach((t, i) => runs.push({ text: t, options: { bullet: { indent: size*1.2 }, hanging: size*0.28, breakLine: i < items.length-1, paraSpaceAfterPt: 9 } }));
  slide.addText(runs, { x, y, w, h, fontFace: FONT, fontSize: size, color, margin: 0.02, valign: 'top', breakLine: false, fit: 'shrink' });
}
function notes(slide, files) { slide.addNotes(`分析依据：\n${files.map(f=>'• '+f).join('\n')}`); }

// 1 — cover
{
  const s = pptx.addSlide(); s.background = { color: C.darkBg };
  // network motif
  const pts = [[8.7,1.0],[10.0,0.7],[11.4,1.35],[9.4,2.0],[10.8,2.6],[12.2,2.15],[8.4,3.1],[9.8,3.7],[11.5,3.55],[12.4,4.5],[10.5,4.8],[8.9,5.2]];
  const edges = [[0,1],[1,2],[0,3],[3,4],[2,5],[4,5],[3,6],[6,7],[4,7],[4,8],[5,8],[8,9],[7,10],[8,10],[10,11],[7,11]];
  edges.forEach(([a,b]) => lineBetween(s,pts[a][0],pts[a][1],pts[b][0],pts[b][1],'3F7772',1.5,'solid','none',18));
  pts.forEach((p,i)=>s.addShape(SH.ellipse,{x:p[0]-0.08,y:p[1]-0.08,w:0.16,h:0.16,fill:{color:i%4===0?C.lime:C.mint},line:{color:C.darkBg,width:1}}));
  pill(s, 'C#  ·  .NET 8  ·  FLOW-BASED RUNTIME', 0.72, 0.72, 3.55, C.teal);
  addText(s, 'DP.WorkFlow', 0.72, 1.55, 7.3, 0.8, 42, C.white, { bold:true, margin:0 });
  addText(s, '流程图代码结构与逻辑框架梳理', 0.72, 2.45, 7.4, 0.65, 26, 'D8F0EC', { bold:true, margin:0 });
  addText(s, '从画布模型、编译校验、运行时调度，到节点插件、JSON 持久化与视觉 UI', 0.75, 3.38, 6.85, 0.9, 15, 'AFCBC7', { valign:'top', margin:0 });
  rr(s,0.72,5.55,6.55,0.95,'173A38','2C5B57',0.12,false);
  addText(s,'核心判断',0.98,5.77,1.0,0.25,11,C.lime,{bold:true,margin:0});
  addText(s,'“可编辑模型 → 可验证定义 → 可观测执行”三段式内核，外接领域节点与 UI。',2.0,5.69,4.9,0.42,14,C.white,{bold:true,margin:0});
  addText(s,'代码审阅范围：用户指定的 12 个目录（其中 DP.WorkFlow.Scripting 为空壳目录）',0.75,6.85,8.2,0.2,9,'86A6A2',{margin:0});
}

// 2 — executive summary
{
  const s = pptx.addSlide('CONTENT'); addTitle(s,'一页结论','体系不是“节点直接互调”，而是目录驱动、先编译、后调度的插件化流程引擎');
  const stats = [
    ['11','有效项目','12 个指定目录中，Scripting 无源码/项目'],['84','已注册节点','标准 / 复合 / 运控 / 工艺 / 视觉'],['15.3k','行 C#','指定有效目录，不含 bin / obj'],['189','测试通过','逐个运行仓库内全部 12 个测试项目']
  ];
  stats.forEach((a,i)=>{ const x=0.62+i*3.12; rr(s,x,1.62,2.78,1.3,i===1?'DDF3ED':C.white,'D3E2E0',0.12,true); addText(s,a[0],x+0.2,1.78,1.15,0.55,29,i===1?C.teal:C.ink,{bold:true,margin:0}); addText(s,a[1],x+1.35,1.78,1.18,0.26,12,C.ink,{bold:true,margin:0}); addText(s,a[2],x+1.35,2.08,1.15,0.45,9.5,C.gray,{valign:'top',margin:0}); });
  card(s,0.62,3.35,3.85,2.75,'01  架构','分层清晰','Abstractions 定义 seam；Core 负责图与编译；Runtime 负责执行；Nodes 与 UI 位于外层。',C.teal);
  card(s,4.74,3.35,3.85,2.75,'02  逻辑','控制流 / 数据流分离','端口连接决定控制流；WorkflowInput + BindingResolver 提供强类型数据流，并考虑并行可见性。',C.orange);
  card(s,8.86,3.35,3.85,2.75,'03  工程','可扩展且可观测','节点模型与 Handler 解耦；快照、Trace、暂停、Hold、恢复、子流程均由 Runtime 统一治理。',C.mint);
  addText(s,'需要优先治理：巨型 DesignerSession / Engine、脚本信任边界、DP.WorkFlow.Scripting 空壳、测试项目未全部纳入解决方案。',0.7,6.48,11.9,0.34,12,C.red,{bold:true,align:'center',margin:0});
  notes(s,['各指定目录源文件统计','DP.WorkFlow.Nodes.*/DP.WorkFlow*Nodes.cs','逐项目 dotnet test 输出']);
}

// 3 — dependency architecture
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'模块依赖：内核向内稳定，能力向外扩展','箭头表示编译期 ProjectReference / 运行期接口依赖方向');
  const layers=[
    {y:5.72,h:0.72,fill:'CFE8E4',items:[['DP.WorkFlow.Abstractions','节点 / 端口 / 结果 / seam']]},
    {y:4.66,h:0.78,fill:'DCECE9',items:[['DP.WorkFlow.Core','画布 / 校验 / 编译'],['DP.WorkFlow.Runtime','Context / Engine / Host']]},
    {y:3.15,h:1.15,fill:'EAF2F1',items:[['Nodes.Standard','26'],['Nodes.Composite','1'],['Nodes.Motion','17'],['Nodes.Process','27'],['Nodes.Vision','13'],['Persistence.Json','Schema 3']]},
    {y:1.62,h:1.12,fill:'FFFFFF',items:[['UI.Shared','设计器 / 属性 / 监视'],['Vision.UI','图像 Hub / ROI']]}
  ];
  layers.forEach((l,li)=>{
    const totalW=l.items.length===1?4.4:(li===1?8.3:li===2?11.9:8.4); const gap=0.18; const itemW=(totalW-gap*(l.items.length-1))/l.items.length; let x=(13.333-totalW)/2;
    l.items.forEach(([t,sub],i)=>{ rr(s,x,l.y,itemW,l.h,l.fill,li===0?C.teal:C.line,0.12,li!==0); addText(s,t,x+0.1,l.y+0.12,itemW-0.2,0.28,li===2?12:15,C.ink,{bold:true,align:'center',margin:0}); addText(s,sub,x+0.1,l.y+0.43,itemW-0.2,l.h-0.48,9.5,li===0?C.teal:C.gray,{align:'center',margin:0}); x+=itemW+gap; });
  });
  arrow(s,6.67,4.62,6.67,4.34,C.teal,2); arrow(s,6.67,3.12,6.67,2.78,C.teal,2);
  pill(s,'外层：适配与交互',10.75,1.22,1.75,C.orange); pill(s,'内层：稳定语义',0.82,6.66,1.72,C.teal);
  addText(s,'额外适配器：ScriptEngine 提供 Roslyn；MachineVision.Abstractions 提供厂商无关视觉 seam。',3.0,6.65,7.35,0.28,10.5,C.gray,{align:'center',margin:0});
  notes(s,['各 .csproj 的 ProjectReference','DP.WorkFlow/src/*/*.csproj']);
}

// 4 — model
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'核心对象模型：配置、布局、连线各司其职','NodeModel 不执行；Handler 才承载运行行为');
  node(s,0.7,2.05,2.75,1.45,'IWorkflowNodeModel','Id · Title · NodeType\n可持久化配置',C.white,C.teal);
  node(s,5.25,1.6,2.85,1.45,'WorkflowCanvasNode','Node + X/Y/W/H\n端口边 / 隐藏端口',C.white,C.orange);
  node(s,5.25,4.05,2.85,1.45,'WorkflowConnection','Model：From/To Node + Port\n路径点 / 标签位置',C.white,C.mint);
  node(s,9.85,2.75,2.75,1.55,'WorkflowCanvasModel','Name\nNodes + Connections', 'DDF3ED',C.teal);
  arrow(s,3.46,2.78,5.2,2.32,C.teal); arrow(s,8.12,2.33,9.8,3.18,C.orange); arrow(s,8.12,4.78,9.8,3.92,C.mint);
  rr(s,0.72,4.55,3.45,1.65,'173A38','173A38',0.12,false);
  addText(s,'关键 seam',0.98,4.78,1.05,0.28,11,C.lime,{bold:true,margin:0});
  bullets(s,['WorkflowNodeDescriptor：稳定类型键、版本、工厂、端口、输出类型','WorkflowNodeCatalog：线程安全注册，重复键直接失败','IWorkflowDynamicPortProvider：按实例配置动态裁剪端口'],1.0,5.12,2.9,0.9,10.2,C.white);
  addText(s,'收益：设计器可编辑同一份模型；持久化只关心配置；运行时可替换 Handler。',4.42,6.25,8.0,0.42,13,C.ink,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Abstractions/Nodes/IWorkflowNodeModel.cs','DP.WorkFlow.Abstractions/Plugins/WorkflowNodeCatalog.cs','DP.WorkFlow.Core/Models/WorkflowCanvasModel.cs']);
}

// 5 — compile pipeline
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'编译流水线：把“可编辑画布”收敛成“只读定义”','运行前一次性发现结构、端口、绑定、并行与子画布问题');
  const stages=[
    ['01','画布输入','节点 + 连线\n起始节点'],['02','结构校验','ID / 端口 / 基数\n连接完整性'],['03','绑定分析','支配关系 / 可达性\n成员路径 / 类型'],['04','结构识别','ParallelAll 汇聚\n递归编译子画布'],['05','只读定义','节点索引 / 出边索引\nDiagnostics']
  ];
  stages.forEach((a,i)=>{ const x=0.58+i*2.54; rr(s,x,2.1,2.15,2.45,i===4?'DDF3ED':C.white,i===4?C.teal:C.line,0.12,true); pill(s,a[0],x+0.18,2.28,0.55,i===2?C.orange:C.teal); addText(s,a[1],x+0.18,2.77,1.8,0.38,16,C.ink,{bold:true,margin:0}); addText(s,a[2],x+0.18,3.32,1.78,0.78,11.5,C.ink2,{valign:'top',margin:0}); if(i<4) arrow(s,x+2.17,3.33,x+2.47,3.33,C.teal,2); });
  rr(s,0.72,5.2,11.88,1.25,'183A38','183A38',0.12,false);
  const flags=[['Error','阻止编译',C.red],['Warning','保留诊断',C.orange],['循环子画布','拒绝',C.red],['不可达节点','WF101',C.lime]];
  flags.forEach((a,i)=>{ const x=1.0+i*2.9; s.addShape(SH.ellipse,{x,y:5.48,w:0.24,h:0.24,fill:{color:a[2]},line:{color:a[2]}}); addText(s,a[0],x+0.36,5.39,1.25,0.28,12,C.white,{bold:true,margin:0}); addText(s,a[1],x+0.36,5.76,1.65,0.25,10,'B8D1CD',{margin:0}); });
  addText(s,'WorkflowExecutionPlan 是运行时的窄接口：GetNodeOrThrow / GetNextNodeIds / GetParallelScope / GetChildDefinition。',1.2,6.55,10.95,0.36,11.5,C.teal,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Core/Compilation/WorkflowCompiler.cs','DP.WorkFlow.Core/Validation/WorkflowValidationError.cs','DP.WorkFlow.Core/Binding/WorkflowBindingAnalysis.cs']);
}

// 6 — execution loop
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'运行主循环：Token 沿“选中端口”推进','节点永远不返回目标 ID；图的连线关系由 WorkflowExecutionPlan 统一解释');
  const xs=[0.75,3.25,5.75,8.25,10.75]; const titles=['等待 Gate','解析节点','执行 Handler','记录输出','选择后继']; const subs=['Pause / Hold\nCancellation','Definition\nNodeId → Model','ExecutionContext\nValueTask<Result>','Run / Token / Scope\nOutput + Trace','SelectedPortKey\n→ NextNodeIds'];
  titles.forEach((t,i)=>{ node(s,xs[i],2.15,1.85,1.55,t,subs[i],i===4?'DDF3ED':C.white,i===2?C.orange:C.teal); if(i<4) arrow(s,xs[i]+1.87,2.92,xs[i+1]-0.04,2.92,C.teal,2); });
  // branch paths
  rr(s,1.0,4.55,3.35,1.25,C.white,C.line,0.12,true); addText(s,'单目标',1.26,4.75,0.9,0.28,14,C.teal,{bold:true,margin:0}); addText(s,'继续 while 循环',2.18,4.73,1.8,0.3,12,C.ink,{bold:true,margin:0}); addText(s,'Success / True / False / Timeout…',1.27,5.17,2.75,0.24,10,C.gray,{margin:0});
  rr(s,4.98,4.55,3.35,1.25,'FFF0E5','F1C6A3',0.12,true); addText(s,'多目标',5.24,4.75,0.9,0.28,14,C.orange,{bold:true,margin:0}); addText(s,'仅 ParallelAll.Branch 合法',6.12,4.72,1.95,0.34,11,C.ink,{bold:true,margin:0}); addText(s,'创建并行作用域与子 Token',5.25,5.17,2.7,0.24,10,C.gray,{margin:0});
  rr(s,8.95,4.55,3.35,1.25,'FDEAEA','E7B9B9',0.12,true); addText(s,'失败',9.21,4.75,0.75,0.28,14,C.red,{bold:true,margin:0}); addText(s,'Fail / Stop / Interrupt',10.0,4.72,1.95,0.34,11,C.ink,{bold:true,margin:0}); addText(s,'可重试故障节点或跳转指定节点',9.22,5.17,2.75,0.24,10,C.gray,{margin:0});
  addText(s,'安全阀：单节点执行次数与总执行次数均设上限，防止无限循环。',2.25,6.42,8.85,0.35,12,C.ink,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Runtime/Engine/WorkflowEngine.cs','DP.WorkFlow.Abstractions/Runtime/NodeExecutionResult.cs']);
}

// 7 — concurrency and child
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'并行与子流程：结构化并发，而不是任意 Task 散射','Token / Scope 身份保证输出可见性；子引擎受父引擎暂停与 Hold 监管');
  // left fork join
  addText(s,'并行作用域',0.72,1.52,3.3,0.35,17,C.ink,{bold:true,margin:0});
  node(s,0.82,2.43,1.7,1.05,'ParallelAll','Branch × N',C.white,C.orange);
  ['A','B','C'].forEach((t,i)=>node(s,3.18,1.55+i*1.34,1.55,0.9,`分支 ${t}`,`Token ${i+2}`,C.white,C.teal));
  node(s,5.48,2.43,1.85,1.05,'WaitAll','共同汇聚点','DDF3ED',C.mint);
  [1.99,3.33,4.67].forEach(y=>{arrow(s,2.54,2.96,3.13,y,C.orange,1.8); arrow(s,4.75,y,5.44,2.96,C.teal,1.8);});
  addText(s,'任一分支异常 → 取消其余分支；全部到达 merge 后父 Token 继续。',0.85,5.35,6.45,0.54,11.5,C.gray,{align:'center',margin:0});
  // right child
  rr(s,7.85,1.55,4.75,4.45,'183A38','183A38',0.15,false); addText(s,'Block 子流程',8.18,1.82,2.3,0.4,18,C.white,{bold:true,margin:0}); pill(s,'CHILD ENGINE',10.65,1.82,1.55,C.orange);
  node(s,8.25,2.55,1.55,1.0,'输入映射','Literal / 变量 / 绑定','E8F0EF',C.teal); node(s,10.43,2.55,1.55,1.0,'子 Context','复制变量\n隔离节点输出','E8F0EF',C.mint); arrow(s,9.82,3.05,10.39,3.05,C.lime,2);
  node(s,8.25,4.25,1.55,1.0,'子定义','预编译','E8F0EF',C.teal); node(s,10.43,4.25,1.55,1.0,'输出映射','回写父变量','E8F0EF',C.orange); arrow(s,9.82,4.75,10.39,4.75,C.lime,2);
  addText(s,'共享 Services 与 Signal；子运行快照嵌入父快照。',8.22,5.55,3.72,0.25,10,'B9D4D0',{align:'center',margin:0});
  addText(s,'输出可见性规则：当前 Token、祖先 Token、以及“已完成并行分支”的输出可读。',1.6,6.48,10.2,0.35,12,C.teal,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Runtime/Engine/WorkflowEngine.cs','DP.WorkFlow.Runtime/Context/WorkflowContext.cs','DP.WorkFlow.Nodes.Composite/Block/BlockNodeHandler.cs']);
}

// 8 — bindings
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'数据绑定：控制流之外的强类型数据通道','WorkflowInput<T> 同时支持 Literal、全局变量、上游节点输出与成员路径');
  node(s,0.75,2.08,2.25,1.35,'生产节点','Output = object?\n附 Run / Token / Scope',C.white,C.teal);
  node(s,3.85,1.35,2.65,1.35,'WorkflowBindingKey','NodeId | Member.Path\n或 $global:key',C.white,C.orange);
  node(s,3.85,3.45,2.65,1.35,'WorkflowInput<T>','Literal / Binding\nValidate()',C.white,C.orange);
  node(s,7.4,2.08,2.4,1.35,'BindingResolver','可见性 → 反射路径\n→ 类型转换', 'DDF3ED',C.teal);
  node(s,10.85,2.08,1.75,1.35,'消费节点','ResolveInput<T>','FFFFFF',C.mint);
  arrow(s,3.02,2.75,3.95,2.0,C.teal); arrow(s,6.38,2.0,7.35,2.55,C.orange); arrow(s,6.38,4.1,7.35,3.0,C.orange); arrow(s,9.83,2.75,10.8,2.75,C.teal);
  rr(s,0.8,5.2,11.75,1.12,'183A38','183A38',0.12,false);
  const checks=[['设计期','支配关系 / 必然执行'],['编译期','成员路径 / 输出类型'],['运行期','Scope 可见 / null / 转换'],['性能','反射访问计划缓存']];
  checks.forEach((a,i)=>{const x=1.08+i*2.9; addText(s,a[0],x,5.43,0.8,0.26,11,C.lime,{bold:true,margin:0}); addText(s,a[1],x+0.85,5.4,1.75,0.34,10.3,C.white,{margin:0});});
  addText(s,'核心价值：连线负责“先后顺序”，绑定负责“值从哪里来”，两者可独立演进。',2.1,6.63,9.2,0.3,12.5,C.ink,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Abstractions/Binding/WorkflowInput.cs','DP.WorkFlow.Core/Binding/WorkflowBindingAnalysis.cs','DP.WorkFlow.Runtime/Binding/WorkflowBindingResolver.cs']);
}

// 9 — node ecosystem
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'节点生态：84 种节点围绕同一运行接口扩展','节点模型声明配置与端口；Handler 通过宿主 IServiceProvider 访问领域能力');
  const data=[['标准',26,C.teal,'流程控制、比较、信号、队列、脚本'],['复合',1,C.lime,'Block 子画布与输入/输出映射'],['运控',17,C.orange,'IO、轴、气缸、真空、读码器'],['工艺',27,'9C6ADE','产品流、工站、晶圆机器人、异常恢复'],['视觉',13,C.mint,'采图、工具、标定、OCR、缺陷、存档']];
  const max=27;
  data.forEach((a,i)=>{ const y=1.7+i*0.92; addText(s,a[0],0.78,y,0.75,0.35,13,C.ink,{bold:true,margin:0}); s.addShape(SH.roundRect,{x:1.65,y:y+0.03,w:7.4*(a[1]/max),h:0.38,rectRadius:0.14,fill:{color:a[2]},line:{color:a[2]}}); addText(s,String(a[1]),1.72+7.4*(a[1]/max),y-0.01,0.5,0.42,14,C.ink,{bold:true,margin:0}); addText(s,a[3],9.55,y,2.75,0.48,10.5,C.gray,{margin:0}); });
  rr(s,0.78,6.25,11.75,0.6,'173A38','173A38',0.12,false);
  addText(s,'统一扩展步骤',1.05,6.41,1.25,0.2,11,C.lime,{bold:true,margin:0});
  addText(s,'Model + Attribute  →  Descriptor/Register  →  Handler/Register  →  宿主注入领域 Adapter',2.45,6.34,9.35,0.34,12,C.white,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Nodes.Standard/WorkflowStandardNodes.cs','DP.WorkFlow.Nodes.Composite/WorkflowCompositeNodes.cs','DP.WorkFlow.Nodes.Motion/WorkflowMotionNodes.cs','DP.WorkFlow.Nodes.Process/WorkflowProcessNodes.cs','DP.WorkFlow.Nodes.Vision/WorkflowVisionNodes.cs']);
}

// 10 persistence
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'JSON 持久化：Schema 3 + 节点版本 + 向后迁移','布局、配置、端口覆盖、子画布与连线路径均被保存');
  const cols=[
    ['写入','Canvas → Document','按 NodeType 查 Descriptor\nConfig 按真实模型类型序列化\n临时文件 + 原子替换',C.teal],
    ['读取','Schema 分流','当前版本直接读取\nSchema 1/2 走 Legacy 迁移\n输出 MigrationReport',C.orange],
    ['兼容','Unknown 保真','未注册节点保留 RawConfig\n高于当前 NodeVersion 则拒绝\n旧 BindingKey 自动归一化',C.mint]
  ];
  cols.forEach((a,i)=>card(s,0.72+i*4.18,1.68,3.76,3.72,a[0],a[1],a[2],a[3]));
  // schema timeline
  addText(s,'Schema 演进',0.85,5.8,1.15,0.25,11,C.ink,{bold:true,margin:0});
  s.addShape(SH.line,{x:2.08,y:5.95,w:9.7,h:0,line:{color:C.line,width:3}});
  [['1','旧格式'],['2','旧绑定'],['3','当前']].forEach((a,i)=>{const x=3.1+i*3.55;s.addShape(SH.ellipse,{x,y:5.78,w:0.34,h:0.34,fill:{color:i===2?C.teal:C.orange},line:{color:C.white,width:2}}); addText(s,a[0],x+0.43,5.7,0.4,0.25,12,C.ink,{bold:true,margin:0}); addText(s,a[1],x+0.43,6.0,0.9,0.22,9.5,C.gray,{margin:0});});
  addText(s,'注意：未知节点可“打开并保留”，但无法通过目录校验并运行。',3.15,6.63,7.2,0.28,11.5,C.red,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Persistence.Json/WorkflowDocumentJsonStore.cs','DP.WorkFlow.Persistence.Json/Documents/WorkflowJsonDocument.cs','DP.WorkFlow.Persistence.Json/Models/UnknownWorkflowNodeModel.cs']);
}

// 11 UI shared
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'UI.Shared：把编辑器能力做成跨 UI 框架的状态模型','WinForms / WPF 只需消费共享会话、属性模型与运行快照');
  // central canvas
  rr(s,4.45,2.12,4.45,2.72,'173A38','173A38',0.16,false); addText(s,'WorkflowDesignerSession',4.8,2.44,3.75,0.4,19,C.white,{bold:true,align:'center',margin:0});
  addText(s,'选择 · 增删 · 连线 · 路由\n复制粘贴 · 对齐分布 · 自动布局\nUndo/Redo · Viewport · Runtime 状态',4.94,3.03,3.45,1.25,12,'C9E0DC',{align:'center',valign:'mid',margin:0});
  const satellites=[
    [0.75,1.6,'DocumentWorkspace','新建 / 打开 / 保存\n脏状态 / 最近文件',C.teal],
    [0.75,4.5,'PropertyInspector','元数据 / 动态可见\n绑定候选 / 结构化编辑',C.orange],
    [9.72,1.6,'DesignerNavigator','根画布 / 子画布\n面包屑导航',C.mint],
    [9.72,4.5,'RuntimeMonitor','Token / Scope / Trace\n耗时趋势 / CSV',C.teal]
  ];
  satellites.forEach(a=>{node(s,a[0],a[1],2.85,1.3,a[2],a[3],C.white,a[4]); const sx=a[0]<4? a[0]+2.87:a[0]-0.05; const sy=a[1]+0.65; const tx=a[0]<4?4.4:8.95; const ty=a[1]<3?2.8:4.15; arrow(s,sx,sy,tx,ty,a[4],1.6);});
  addText(s,'StudioRuntimeBinding 在 SynchronizationContext 上合并最新快照，避免 UI 被高频运行事件淹没。',1.6,6.55,10.2,0.35,12,C.ink,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.UI.Shared/Designer/WorkflowDesignerSession.cs','DP.WorkFlow.UI.Shared/Documents/WorkflowDocumentWorkspace.cs','DP.WorkFlow.UI.Shared/Properties/WorkflowPropertyInspectorModel.cs','DP.WorkFlow.UI.Shared/Runtime/WorkflowRuntimeMonitorModel.cs','DP.WorkFlow.UI.Shared/Runtime/WorkflowStudioRuntimeBinding.cs']);
}

// 12 vision
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'视觉链路：工作流保持厂商无关，UI 优先走原生图像','Provider / Image / Tool / Overlay 接口位于 MachineVision.Abstractions');
  const stages=[
    ['采集','相机 / 文件 / 文件夹','Vision.AcquireImage',C.teal],
    ['执行','ProviderId + ToolId','Vision.RunTool',C.orange],
    ['发布','Result + Overlay','IVisionDisplaySink',C.mint],
    ['背压','单槽 + FPS 节流','VisionDisplayHub',C.teal],
    ['显示','Native lease 优先','WPF / WinForms',C.orange]
  ];
  stages.forEach((a,i)=>{const x=0.55+i*2.58; rr(s,x,2.0,2.18,2.3,i===3?'DDF3ED':C.white,C.line,0.12,true); pill(s,a[0],x+0.2,2.2,0.72,a[3]); addText(s,a[1],x+0.2,2.82,1.78,0.42,14,C.ink,{bold:true,align:'center',margin:0}); addText(s,a[2],x+0.2,3.42,1.78,0.45,10,C.gray,{align:'center',margin:0}); if(i<4) arrow(s,x+2.2,3.12,x+2.52,3.12,C.teal,2);});
  rr(s,0.72,4.92,5.85,1.22,'173A38','173A38',0.12,false); addText(s,'资源与性能',0.98,5.15,1.3,0.3,13,C.lime,{bold:true,margin:0}); addText(s,'IVisionResourceTracker 管生命周期；原生图像通过 Lease 避免过早释放；必要时才转像素帧。',2.25,5.08,3.95,0.52,10.8,C.white,{margin:0});
  rr(s,6.88,4.92,5.72,1.22,'FFF0E5','EAC39F',0.12,false); addText(s,'交互编辑',7.15,5.15,1.2,0.3,13,C.orange,{bold:true,margin:0}); addText(s,'ROI Session/Page 与 OverlayComposer 独立于具体桌面框架，Renderer 负责最终绘制。',8.4,5.08,3.82,0.52,10.8,C.ink,{margin:0});
  addText(s,'视觉节点失败可选择 Failure 端口，或升级为可恢复中断。',3.2,6.54,6.9,0.3,12,C.teal,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Nodes.Vision/AcquireVisionImageNode.cs','DP.WorkFlow.Nodes.Vision/RunVisionToolNode.cs','DP.WorkFlow.Vision.UI/VisionDisplayHub.cs','DP.WorkFlow.Vision.UI/VisionRoiEditorSession.cs']);
}

// 13 quality
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'工程质量：关键路径已有测试，但解决方案覆盖不完整','本次实际逐个执行仓库中全部 12 个测试项目：189 / 189 通过');
  const groups=[['核心与运行',38,'Core 23 + Runtime 15',C.teal],['节点',67,'Composite 5 + Standard 42 + Motion 6 + Process 14',C.orange],['UI',52,'Shared 51 + Windows 1',C.mint],['脚本与视觉',32,'Script 4 + Vision 16 + PaddleOCR 4 + Persistence 8',C.teal]];
  groups.forEach((a,i)=>{const x=0.72+i*3.12; rr(s,x,1.72,2.8,2.05,C.white,C.line,0.12,true); addText(s,String(a[1]),x+0.22,1.97,1.2,0.65,31,a[3],{bold:true,margin:0}); addText(s,a[0],x+1.33,2.02,1.15,0.3,13,C.ink,{bold:true,margin:0}); addText(s,a[2],x+0.22,2.9,2.32,0.48,9.5,C.gray,{valign:'top',margin:0});});
  rr(s,0.72,4.25,5.78,1.72,'E4F1EF','BDD8D4',0.12,false); addText(s,'已验证',1.02,4.54,1.0,0.3,14,C.teal,{bold:true,margin:0}); bullets(s,['完整构建成功','全部测试项目逐一运行，无失败','并行、绑定、持久化、UI 会话均有专门测试'],2.0,4.48,4.05,1.02,11,C.ink);
  rr(s,6.82,4.25,5.78,1.72,'FDEAEA','E7B9B9',0.12,false); addText(s,'缺口',7.12,4.54,0.8,0.3,14,C.red,{bold:true,margin:0}); bullets(s,['DP.WorkFlow.sln 只收录 7 / 12 个测试项目','Core / Runtime / Standard / Persistence / Composite 测试未进入默认 solution test','CI 若只跑 sln，会漏掉 93 项测试'],8.02,4.48,4.08,1.04,11,C.ink);
  addText(s,'建议：把全部测试项目加入解决方案，并将“逐项目发现 + 运行”设为 CI 兜底。',2.1,6.38,9.1,0.4,12.5,C.red,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow/DP.WorkFlow.sln','DP.WorkFlow/tests/*/*.csproj','本次 dotnet test 执行结果']);
}

// 14 strengths / risks
{
  const s=pptx.addSlide('CONTENT'); addTitle(s,'架构评价：深模块已经形成，热点集中在少数大类','优势来自窄接口与集中治理；风险主要来自规模、信任边界与命名漂移');
  const left=[['窄运行接口','NodeExecutionResult 只表达端口与结果，不泄漏图遍历'],['集中治理','Engine 统一并行、暂停、快照、Trace、恢复与子运行'],['真实 seam','Handler、设备服务、视觉 Provider、UI Adapter 均可替换'],['兼容意识','Schema / NodeVersion / Unknown 节点 / Legacy 迁移齐备']];
  const right=[['巨型实现','DesignerSession 1,277 行；Engine 815 行；职责继续增长会降低 locality'],['脚本边界','Roslyn 脚本被明确视为“受信任代码”，可访问宿主 Services'],['项目漂移','DP.WorkFlow.Scripting 为空壳，实际依赖 ScriptEngine；名称会误导维护者'],['静态字符串','NodeType / 端口 / 服务键较多依赖字符串，重构需更强自动校验']];
  addText(s,'优势',0.78,1.52,1.0,0.35,18,C.teal,{bold:true,margin:0}); addText(s,'风险',6.88,1.52,1.0,0.35,18,C.red,{bold:true,margin:0});
  left.forEach((a,i)=>{rr(s,0.75,2.0+i*1.08,5.72,0.86,C.white,C.line,0.12,true); pill(s,String(i+1).padStart(2,'0'),0.95,2.2+i*1.08,0.52,C.teal); addText(s,a[0],1.62,2.11+i*1.08,1.2,0.28,12.5,C.ink,{bold:true,margin:0}); addText(s,a[1],2.85,2.08+i*1.08,3.3,0.42,10.5,C.gray,{margin:0});});
  right.forEach((a,i)=>{rr(s,6.85,2.0+i*1.08,5.72,0.86,i===0?'FFF0E5':'FFFFFF',i===0?'E7B98E':C.line,0.12,true); pill(s,'!',7.05,2.2+i*1.08,0.52,i===0?C.orange:C.red); addText(s,a[0],7.72,2.11+i*1.08,1.2,0.28,12.5,C.ink,{bold:true,margin:0}); addText(s,a[1],8.95,2.08+i*1.08,3.3,0.42,10.5,C.gray,{margin:0});});
  addText(s,'删除测试：若去掉 Engine / Compiler / DesignerSession，复杂度会散落到所有调用者——这些模块正在“赚取深度”。',1.25,6.55,10.85,0.34,11.5,C.ink,{bold:true,align:'center',margin:0});
  notes(s,['DP.WorkFlow.Runtime/Engine/WorkflowEngine.cs','DP.WorkFlow.UI.Shared/Designer/WorkflowDesignerSession.cs','DP.WorkFlow.Nodes.Standard/Scripting/CSharpScriptNode.cs','DP.WorkFlow/src/DP.WorkFlow.Scripting']);
}

// 15 roadmap
{
  const s=pptx.addSlide(); s.background={color:C.darkBg};
  pill(s,'NEXT STEPS',0.72,0.65,1.4,C.teal);
  addText(s,'建议路线：先补工程闭环，再拆热点实现',0.72,1.18,10.7,0.6,30,C.white,{bold:true,margin:0});
  const steps=[
    ['01','1–2 天','修复项目基线','把 5 个遗漏测试项目加入 DP.WorkFlow.sln；删除或说明 DP.WorkFlow.Scripting 空壳。',C.lime],
    ['02','1 周','强化契约自动化','为节点注册完整性、NodeType/端口唯一性、持久化 round-trip 建统一契约测试。',C.mint],
    ['03','2–3 周','拆分热点实现','保持现有外部 interface 不变，将 Engine 拆为调度/并行/恢复内部模块；DesignerSession 拆命令历史/选择/连线/布局。',C.orange],
    ['04','持续','收紧运行安全','为脚本建立显式启用策略与允许程序集清单；为视觉资源、Trace 容量、并行规模设运行预算。',C.red]
  ];
  steps.forEach((a,i)=>{const y=2.12+i*1.05; s.addShape(SH.ellipse,{x:0.78,y:y+0.06,w:0.46,h:0.46,fill:{color:a[4]},line:{color:a[4]}}); addText(s,a[0],0.78,y+0.12,0.46,0.22,10,C.darkBg,{bold:true,align:'center',margin:0}); addText(s,a[1],1.48,y,0.85,0.28,10,'83A8A3',{bold:true,margin:0}); addText(s,a[2],2.42,y-0.03,2.05,0.34,15,C.white,{bold:true,margin:0}); addText(s,a[3],4.7,y-0.05,7.5,0.52,11.5,'C8DDDA',{margin:0}); if(i<3)s.addShape(SH.line,{x:1.0,y:y+0.53,w:0,h:0.52,line:{color:'47706C',width:2}});});
  rr(s,0.72,6.45,11.85,0.62,'173A38','2C5753',0.12,false); addText(s,'目标状态：保持“模型 → 编译 → 执行”的外部 seam 稳定，让复杂度继续向内部收敛。',1.05,6.59,11.2,0.28,13,C.lime,{bold:true,align:'center',margin:0});
  addText(s,'END',12.22,0.72,0.4,0.2,9,'72938F',{fontFace:'Arial',bold:true,margin:0});
}

if (process.env.MAX_SLIDES) pptx._slides = pptx._slides.slice(0, Number(process.env.MAX_SLIDES));
pptx.writeFile({ fileName: process.env.OUT_FILE || 'DP.WorkFlow/docs/DP.WorkFlow_流程图架构梳理.pptx' });
