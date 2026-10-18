using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

class PromptFormCheck {
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"KVMem.exe"));
  var type=assembly.GetType("App");
  var field=type.GetField("systemPrompt",BindingFlags.Instance|BindingFlags.NonPublic);
  var save=type.GetMethod("SaveSystemPrompt",BindingFlags.Instance|BindingFlags.NonPublic);
  var sample="你是一名中文助手。\r\n请清晰回答，保留 \"引号\" 和 \\ 路径。";
  using(var form=(Form)Activator.CreateInstance(type)) {
   var text=(TextBox)field.GetValue(form);if(text.Text!="")throw new Exception("Expected initially empty prompt");
   text.Text=sample;save.Invoke(form,null);
  }
  using(var form=(Form)Activator.CreateInstance(type)) {
   var text=(TextBox)field.GetValue(form);if(text.Text!=sample)throw new Exception("Multiline prompt did not round-trip");
   var tabs=(TabControl)type.GetField("tabs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   if(tabs.TabPages[4].Text!="系统提示词")throw new Exception("Prompt tab missing");
   text.Clear();save.Invoke(form,null);
  }
  using(var form=(Form)Activator.CreateInstance(type))if(((TextBox)field.GetValue(form)).Text!="")throw new Exception("Cleared prompt did not persist");
  if(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"system-prompt.json.bak")))throw new Exception("No backup written");
  Console.WriteLine("PASS: multiline save/reopen, clear/reopen, backup and prompt tab existence");
 }
}
