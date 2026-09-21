using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;

public class Settings {
 public string Executable="";
 public string Model="";
 public string Node="node.exe";
 public bool LanEnabled=true,Thinking=true;
 public bool VisionEnabled=false,VisionGpu=false; public string Mmproj=""; public int ImageMaxTokens=512;
 public int Context=262144,Budget=45056,Reserve=20480,ThinkingBudget=16384;
 public string LanAddress=""; public int LanPrefix=24;
}
public class NetworkItem { public string Address,Name;public int Prefix;public override string ToString(){return Name+"  ·  "+Address+"/"+Prefix;} }
public class Client:WebClient { protected override WebRequest GetWebRequest(Uri u){var r=base.GetWebRequest(u);r.Timeout=1200;r.Proxy=null;return r;} }
public class App:Form {
 static string Root=AppDomain.CurrentDomain.BaseDirectory;
 static JavaScriptSerializer Json=new JavaScriptSerializer();
 Settings cfg; bool busy=false,polling=false; bool closing=false;
 Color bg=Color.FromArgb(16,24,36),card=Color.FromArgb(25,37,52),ink=Color.FromArgb(233,241,246),muted=Color.FromArgb(157,177,192),accent=Color.FromArgb(54,211,174);
 Label status,detail,notice,openAI,ollama,modelLabel,networkLabel; ComboBox network;CheckBox lan,thinking;TextBox model,exe,log;
 NumericUpDown context,budget,reserve,thinkingBudget;Button start,stop,firewall;Timer timer;TabControl tabs;
 public App(){
  cfg=LoadSettings();Text="KVMem Desktop · Qwen 256K";ClientSize=new Size(1020,750);MinimumSize=new Size(1036,789);StartPosition=FormStartPosition.CenterScreen;
  BackColor=bg;ForeColor=ink;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
  if(File.Exists(Path.Combine(Root,"KVMem.ico")))Icon=new Icon(Path.Combine(Root,"KVMem.ico"));
  var title=L("KVMem",28,22,300,48,28,ink);Controls.Add(title);Controls.Add(L("DESKTOP  /  本地推理工作台",31,72,560,25,10,muted));
  Controls.Add(L("QWEN 27B  ·  KVMEM 256K",650,45,340,28,11,accent));
  tabs=new TabControl{Location=new Point(26,118),Size=new Size(968,565),Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(tabs);
  var home=Page("运行与连接"); var settings=Page("启动设置"); var logs=Page("运行日志");
  var visual=Page("视觉设置");
  visual.Controls.Add(L("图片识别 · 保存后重启服务生效",24,18,870,34,18,ink));
  visual.Controls.Add(L("图片使用 OpenAI 接口；Ollama 兼容桥仍仅支持文本。",26,65,880,40,10,muted));
  var vision=new CheckBox{Text="启用视觉",Checked=cfg.VisionEnabled,Location=new Point(27,120),Size=new Size(250,30)};visual.Controls.Add(vision);
  var mmproj=FileField(visual,"视觉投影 GGUF (mmproj)",170,cfg.Mmproj,"Vision projector|*.gguf");
  var visionGpu=new CheckBox{Text="使用 GPU 编码图片（额外占用显存）",Checked=cfg.VisionGpu,Location=new Point(27,250),Size=new Size(850,30)};visual.Controls.Add(visionGpu);
  var imageTokens=N(visual,"每张图片 token 上限",310,27,64,4096,cfg.ImageMaxTokens);
  visual.Controls.Add(L("16 GB 显存建议先用 CPU 编码、512 tokens。更高上限保留更多细节，也会增加处理时间。",26,400,880,55,10,muted));
  var saveVision=B("保存视觉设置",27,478,220,40,accent);saveVision.Click+=(s,e)=>{try{if(vision.Checked&&!File.Exists(mmproj.Text))throw new Exception("请选择有效的视觉投影文件。");cfg.VisionEnabled=vision.Checked;cfg.Mmproj=mmproj.Text;cfg.VisionGpu=visionGpu.Checked;cfg.ImageMaxTokens=(int)imageTokens.Value;Save();Say("视觉设置已保存，重启服务后生效。");}catch(Exception x){Error(x);}};visual.Controls.Add(saveVision);
  status=L("正在检查服务…",24,20,840,42,21,ink);home.Controls.Add(status);
  detail=L("",26,67,870,45,10,muted);home.Controls.Add(detail);
  start=B("启动服务",26,123,170,46,accent);start.Click+=async(s,e)=>await StartServices();home.Controls.Add(start);
  stop=B("停止服务",211,123,145,46,card);stop.Click+=async(s,e)=>await StopServices();home.Controls.Add(stop);
  var chat=B("打开聊天",372,123,145,46,card);chat.Click+=(s,e)=>Open("http://127.0.0.1:18200/");home.Controls.Add(chat);
  var local=B("复制本机地址",533,123,170,46,card);local.Click+=(s,e)=>Copy("http://127.0.0.1:18200/v1");home.Controls.Add(local);
  home.Controls.Add(L("MAC / 局域网连接",26,194,500,28,13,accent));
  network=new ComboBox{Location=new Point(27,233),Size=new Size(650,30),ForeColor=Color.Black,BackColor=Color.White,DropDownStyle=ComboBoxStyle.DropDownList};home.Controls.Add(network); network.DrawMode=DrawMode.OwnerDrawFixed; network.ItemHeight=24; network.DrawItem+=(s,e)=>{e.DrawBackground(); if(e.Index>=0)using(var brush=new SolidBrush(Color.Black))e.Graphics.DrawString(network.Items[e.Index].ToString(),network.Font,brush,e.Bounds);};
  network.Visible=false; networkLabel=L("",27,233,655,32,12,ink); home.Controls.Add(networkLabel); var choose=B("选择网卡",720,229,176,36,card); choose.Click+=(s,e)=>{var menu=new ContextMenuStrip();for(int i=0;i<network.Items.Count;i++){int index=i;menu.Items.Add(network.Items[i].ToString(),null,(sender,evt)=>{network.SelectedIndex=index;UpdateAddresses();});}menu.Show(choose,new Point(0,choose.Height));};home.Controls.Add(choose); foreach(var n in Networks())network.Items.Add(n);
  for(int i=0;i<network.Items.Count;i++)if(((NetworkItem)network.Items[i]).Address==cfg.LanAddress)network.SelectedIndex=i;
  if(network.SelectedIndex<0&&network.Items.Count>0)network.SelectedIndex=0;
  network.SelectedIndexChanged+=(s,e)=>UpdateAddresses();
  home.Controls.Add(L("OpenAI / DSH",27,283,170,25,10,muted));openAI=L("",205,280,530,30,12,ink);home.Controls.Add(openAI);
  var copyAI=B("复制",805,280,92,32,card);copyAI.Click+=(s,e)=>Copy(openAI.Text);home.Controls.Add(copyAI);
  home.Controls.Add(L("Ollama 兼容",27,326,175,25,10,muted));ollama=L("",205,323,530,30,12,ink);home.Controls.Add(ollama);
  var copyO=B("复制",805,323,92,32,card);copyO.Click+=(s,e)=>Copy(ollama.Text);home.Controls.Add(copyO);
  modelLabel=L("",27,371,865,40,10,muted);home.Controls.Add(modelLabel);
  firewall=B("允许局域网访问",26,421,220,42,accent);firewall.Click+=async(s,e)=>await Firewall("firewall");home.Controls.Add(firewall);
  var revoke=B("撤销本应用放行规则",262,421,225,42,card);revoke.Click+=async(s,e)=>await Firewall("remove-firewall");home.Controls.Add(revoke);
  var guide=B("查看 Mac 配置说明",503,421,220,42,card);guide.Click+=(s,e)=>Open(Path.Combine(Root,"Mac连接说明.txt"));home.Controls.Add(guide);
  home.Controls.Add(L("仅在受信任局域网使用；允许访问时 Windows 会请求管理员确认。",27,482,875,27,10,muted));
  settings.Controls.Add(L("下一次启动生效",24,18,600,34,18,ink));settings.Controls.Add(L("保存不会中断当前会话。修改后请停止服务，再重新启动。",26,59,840,28,10,muted));
  model=FileField(settings,"GGUF 模型",105,cfg.Model,"GGUF model|*.gguf");exe=FileField(settings,"KVMem 程序",171,cfg.Executable,"KVMem server|llama-kvmem-server.exe");
  context=N(settings,"上下文容量",260,26,2048,1048576,cfg.Context);budget=N(settings,"GPU 历史预算",260,259,128,262144,cfg.Budget);reserve=N(settings,"输出预留 / 上限",260,492,128,65536,cfg.Reserve);thinkingBudget=N(settings,"思考预算",260,725,0,65536,cfg.ThinkingBudget);
  lan=new CheckBox{Text="允许局域网连接",Checked=cfg.LanEnabled,Location=new Point(27,360),Size=new Size(250,30)};thinking=new CheckBox{Text="默认开启思考",Checked=cfg.Thinking,Location=new Point(310,360),Size=new Size(250,30)};settings.Controls.Add(lan);settings.Controls.Add(thinking);
  settings.Controls.Add(L("当前方案：Q8 KV · MTP 3 · F16 draft · batch 512\n预算增加会占用更多显存；256K 是总上下文，不是单次可输出长度。",26,405,880,58,10,muted));
  var save=B("保存设置",27,478,180,40,accent);save.Click+=(s,e)=>{try{Save();Say("设置已保存，下次启动生效。");}catch(Exception x){Error(x);}};settings.Controls.Add(save);
  log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BackColor=bg,ForeColor=ink,Font=new Font("Consolas",9),Location=new Point(18,66),Size=new Size(910,440),Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right};logs.Controls.Add(log);
  var refresh=B("刷新日志",18,15,140,36,card);refresh.Click+=(s,e)=>ReadLog();logs.Controls.Add(refresh);var folder=B("打开日志文件夹",175,15,210,36,card);folder.Click+=(s,e)=>{Directory.CreateDirectory(Path.Combine(Root,"logs"));Open(Path.Combine(Root,"logs"));};logs.Controls.Add(folder);
  notice=L("关闭此面板不会停止模型服务。",29,701,955,28,10,muted);notice.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;Controls.Add(notice);
  UpdateAddresses();timer=new Timer{Interval=5000};timer.Tick+=async(s,e)=>await RefreshStatus();Shown+=async(s,e)=>{timer.Start();await RefreshStatus();};FormClosing+=(s,e)=>{closing=true;timer.Stop();};
 }
 TabPage Page(string name){var p=new TabPage(name){BackColor=card,ForeColor=ink};tabs.TabPages.Add(p);return p;}
 Label L(string text,int x,int y,int w,int h,float size,Color color){return new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("Microsoft YaHei UI",size),ForeColor=color};}
 Button B(string text,int x,int y,int w,int h,Color color){var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,h),BackColor=color,ForeColor=color==accent?bg:ink,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(65,85,103);return b;}
 TextBox FileField(Control parent,string name,int y,string value,string filter){parent.Controls.Add(L(name,26,y,830,25,10,muted));var t=new TextBox{Text=value,Location=new Point(27,y+28),Size=new Size(770,28)};parent.Controls.Add(t);var b=B("浏览",812,y+26,85,32,card);b.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter=filter,FileName=t.Text})if(d.ShowDialog()==DialogResult.OK)t.Text=d.FileName;};parent.Controls.Add(b);return t;}
 NumericUpDown N(Control p,string name,int y,int x,int min,int max,int value){p.Controls.Add(L(name,x,y,210,26,10,muted));var n=new NumericUpDown{Minimum=min,Maximum=max,Value=value,Increment=128,Location=new Point(x,y+35),Size=new Size(190,30),ThousandsSeparator=true};p.Controls.Add(n);return n;}
 static Settings LoadSettings(){string p=Path.Combine(Root,"settings.json");return File.Exists(p)?Json.Deserialize<Settings>(File.ReadAllText(p)):new Settings();}
 void Save(){if(!File.Exists(model.Text)||!File.Exists(exe.Text))throw new Exception("请检查模型和 KVMem 程序路径。");if((int)thinkingBudget.Value>(int)reserve.Value)throw new Exception("思考预算不能超过输出预留。");if(budget.Value+reserve.Value>context.Value)throw new Exception("GPU 历史预算与输出预留之和不能超过上下文容量。");if((int)budget.Value%128!=0||(int)reserve.Value%128!=0)throw new Exception("历史预算与输出预留应为128的倍数。");cfg.Model=model.Text;cfg.Executable=exe.Text;cfg.Context=(int)context.Value;cfg.Budget=(int)budget.Value;cfg.Reserve=(int)reserve.Value;cfg.ThinkingBudget=(int)thinkingBudget.Value;cfg.Thinking=thinking.Checked;cfg.LanEnabled=lan.Checked;var n=network.SelectedItem as NetworkItem;if(n!=null){cfg.LanAddress=n.Address;cfg.LanPrefix=n.Prefix;}File.WriteAllText(Path.Combine(Root,"settings.json"),Json.Serialize(cfg),new UTF8Encoding(false));UpdateAddresses();}
 static List<NetworkItem> Networks(){var items=new List<NetworkItem>();foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){if(n.OperationalStatus!=OperationalStatus.Up||n.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;foreach(var a in n.GetIPProperties().UnicastAddresses){if(a.Address.AddressFamily!=AddressFamily.InterNetwork||a.IPv4Mask==null)continue;var b=a.IPv4Mask.GetAddressBytes();int prefix=0;foreach(byte v in b)for(int i=0;i<8;i++)if((v&(1<<i))!=0)prefix++;items.Add(new NetworkItem{Address=a.Address.ToString(),Prefix=prefix,Name=n.Name});}}return items.OrderBy(n=>n.Address.StartsWith("192.168.")?0:1).ToList();}
 void UpdateAddresses(){var n=network.SelectedItem as NetworkItem;string ip=n==null?"未找到局域网地址":n.Address; networkLabel.Text=n==null?"未检测到局域网网卡":n.ToString();openAI.Text="http://"+ip+":18200/v1";ollama.Text="http://"+ip+":18201";modelLabel.Text="OpenAI 模型："+Path.GetFileName(cfg.Model)+"\nOllama 兼容模型：qwen-kvmem:256k";}
 static string Get(string url){try{using(var c=new Client())return c.DownloadString(url);}catch{return "";}}
 async Task RefreshStatus(){if(polling||closing)return;polling=true;try{string[] result=await Task.Run(()=>new[]{Get("http://127.0.0.1:18200/v1/models"),Get("http://127.0.0.1:18201/api/version")});if(closing)return;bool active=result[0].Contains(Path.GetFileName(cfg.Model));bool bridge=result[1].Contains("kvmem-bridge");status.Text=active?"●  模型服务运行中":"○  模型服务未就绪";status.ForeColor=active?accent:ink;detail.Text=(active?"本机 API 已响应":"可启动服务；首次加载需要数秒。")+"  ·  Ollama 兼容接口"+(bridge?"运行中":"未启动")+"\n配置："+cfg.Context.ToString("N0")+" tokens（下次启动设置）";if(!busy){start.Enabled=true;stop.Enabled=active;}if(tabs.SelectedIndex==2)ReadLog();}finally{polling=false;}}
 void Say(string text){notice.Text=text;}
 void Error(Exception x){Say("操作未完成："+x.Message);MessageBox.Show(this,x.Message,"KVMem Desktop",MessageBoxButtons.OK,MessageBoxIcon.Information);}
 void Copy(string text){try{Clipboard.SetText(text);Say("已复制："+text);}catch(Exception x){Error(x);}}
 void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception x){Error(x);}}
 async Task RunAction(string action,bool admin){string script=Path.Combine(Root,"control.ps1");var info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -Action "+action){WorkingDirectory=Root,UseShellExecute=admin,CreateNoWindow=!admin,WindowStyle=ProcessWindowStyle.Hidden};if(admin)info.Verb="runas";else{info.RedirectStandardOutput=true;info.RedirectStandardError=true;}await Task.Run(()=>{using(var p=Process.Start(info)){string error=admin?"":p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception("操作失败。"+error+" 请查看日志或确认管理员授权。");}});}
 async Task StartServices(){if(busy)return;busy=true;start.Enabled=false;try{Save();Say("正在启动，模型加载期间请稍候…");await RunAction("start",false);for(int i=0;i<25;i++){if(Get("http://127.0.0.1:18200/health").Contains("ok"))break;await Task.Delay(1000);}Say("启动操作完成；状态会自动刷新。");await RefreshStatus();}catch(Exception x){Error(x);}finally{busy=false;start.Enabled=true;}}
 static int PortPid(int port){var info=new ProcessStartInfo("netstat.exe","-ano -p tcp"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};using(var p=Process.Start(info)){string output=p.StandardOutput.ReadToEnd();p.WaitForExit();foreach(string line in output.Split('\n')){var parts=line.Split(new[]{' ','\t','\r'},StringSplitOptions.RemoveEmptyEntries);if(parts.Length>=5&&parts[1].EndsWith(":"+port)&&parts[3]=="LISTENING")return int.Parse(parts[4]);}}return 0;}
 static void StopPort(int port,string expectedExe){int id=PortPid(port);if(id==0)return;using(var p=Process.GetProcessById(id)){if(!string.Equals(Path.GetFullPath(p.MainModule.FileName),Path.GetFullPath(expectedExe),StringComparison.OrdinalIgnoreCase))throw new Exception("端口被其他程序占用，未停止该进程。");p.Kill();p.WaitForExit(5000);}}
 async Task StopServices(){if(busy)return;if(MessageBox.Show(this,"停止后，正在生成的回答及 Mac 连接会中断。继续？","停止 KVMem",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;busy=true;try{await Task.Run(()=>{if(PortPid(18200)>0)StopPort(18200,cfg.Executable);if(Get("http://127.0.0.1:18201/api/version").Contains("0.0.0-kvmem-bridge"))StopPort(18201,cfg.Node);});Say("服务已停止，显存将释放。");await RefreshStatus();}catch(Exception x){Error(x);}finally{busy=false;}}
 async Task Firewall(string action){if(busy)return;busy=true;try{Save();if(action=="firewall"&&!cfg.LanEnabled)throw new Exception("请先在启动设置中启用局域网连接，并重启服务。");Say("请在 Windows 提示中确认管理员权限…");await RunAction(action,true);Say(action=="firewall"?"已允许所选局域网。请在 Mac 打开连接地址验证。":"已撤销本应用创建的防火墙规则。");}catch(Exception x){Error(x);}finally{busy=false;}}
 void ReadLog(){var p=Path.Combine(Root,"logs","server.log");try{if(!File.Exists(p)){log.Text="此面板尚未启动过模型。现有服务的日志由原启动器管理。";return;}using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(f.Length>60000)f.Seek(-60000,SeekOrigin.End);using(var r=new StreamReader(f))log.Text=r.ReadToEnd();}log.SelectionStart=log.TextLength;log.ScrollToCaret();}catch(Exception x){log.Text=x.Message;}}
 static void MakeIcon(){using(var b=new Bitmap(256,256))using(var g=Graphics.FromImage(b)){g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(16,24,36));using(var pen=new Pen(Color.FromArgb(54,211,174),12)){g.DrawEllipse(pen,20,20,216,216);}using(var font=new Font("Segoe UI",68,FontStyle.Bold))using(var brush=new SolidBrush(Color.FromArgb(233,241,246)))g.DrawString("KV",font,brush,new RectangleF(0,60,256,150),new StringFormat{Alignment=StringAlignment.Center});b.Save(Path.Combine(Root,"KVMem.png"));using(var icon=Icon.FromHandle(b.GetHicon()))using(var f=File.Create(Path.Combine(Root,"KVMem.ico")))icon.Save(f);}}
 [STAThread] public static void Main(string[] args){try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);if(args.Contains("--make-icon")){MakeIcon();return;}if(args.Contains("--self-test")){var s=LoadSettings();var lines=new List<string>{"ModelExists="+File.Exists(s.Model),"ExecutableExists="+File.Exists(s.Executable),"NodeExists="+File.Exists(s.Node),"ModelEndpoint="+Get("http://127.0.0.1:18200/v1/models"),"BridgeEndpoint="+Get("http://127.0.0.1:18201/api/version"),"LAN="+string.Join(";",Networks().Select(n=>n.ToString()))};File.WriteAllLines(Path.Combine(Root,"self-test.txt"),lines);return;}using(var app=new App()){if(args.Contains("--preview")){app.Show();for(int j=0;j<40;j++){Application.DoEvents();System.Threading.Thread.Sleep(100);}for(int i=0;i<app.tabs.TabPages.Count;i++){app.tabs.SelectedIndex=i;Application.DoEvents();using(var b=new Bitmap(app.Width,app.Height)){app.DrawToBitmap(b,new Rectangle(Point.Empty,app.Size));b.Save(Path.Combine(Root,"preview-"+i+".png"));}}app.Close();return;}Application.Run(app);}}catch(Exception e){File.WriteAllText(Path.Combine(Root,"error.txt"),e.ToString());if(!args.Any())MessageBox.Show(e.Message,"KVMem Desktop");Environment.ExitCode=1;}}
}

