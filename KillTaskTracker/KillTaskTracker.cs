using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

using Newtonsoft.Json;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace KillTaskTracker
{
    // FriendlyName is the name that will show up in the plugins list of the decal agent (the one in windows, not in-game)
    [FriendlyName("KillTaskTracker")]
    public class KillTaskTracker : PluginBase
    {
        Dictionary<String, List<KillTask>> start_messages_;
        Dictionary<String, KillTask> monster_substrings_;
        Dictionary<String, KillTask> end_messages_;
        List<KillTask> active_tasks_;

        Regex kill_regex_;
        String dll_directory_;
        String progress_filename_;

        String startup_exception_;

        public KillTaskTracker()
        {
            startup_exception_ = null;

            try
            {
                start_messages_ = new Dictionary<String, List<KillTask>>();
                monster_substrings_ = new Dictionary<String, KillTask>();
                end_messages_ = new Dictionary<String, KillTask>();
                active_tasks_ = new List<KillTask>();
                kill_regex_ = new Regex("You have killed ([0-9]+) ([A-Za-z ]+)! (You must kill [0-9]+ to complete your task.)|(Your task is complete!)\n");
                dll_directory_ = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                LoadTasks(dll_directory_ + "\\task_definitions.json");
            }
            catch (Exception ex)
            {
                startup_exception_ = ex.ToString();
            }
        }

        Boolean LoadTasks(String filename)
        {
            using (StreamReader r = new StreamReader(filename))
            {
                string json = r.ReadToEnd();
                foreach (KillTask task in JsonConvert.DeserializeObject<List<KillTask>>(json))
                {
                    if (start_messages_.ContainsKey(task.start_message))
                    {
                        start_messages_[task.start_message].Add(task);
                    }
                    else
                    {
                        List<KillTask> task_list = new List<KillTask>();
                        task_list.Add(task);
                        start_messages_.Add(task.start_message, task_list);
                    }
                    foreach (String monster_substring in task.monster_substrings)
                    {
                        if (monster_substrings_.ContainsKey(monster_substring))
                        {
                            throw new Exception("Duplicate monster substring: \"" + monster_substring + "\".");
                        }
                        monster_substrings_.Add(monster_substring, task);
                    }
                    foreach (String end_message in task.end_messages)
                    {
                        end_messages_.Add(end_message, task);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// This is called when the plugin is started up. This happens only once.
        /// We init most of our objects here, EXCEPT ones that depend on other assemblies (not counting decal assemblies).
        /// </summary>
        protected override void Startup()
        {
            try
            {
                CoreManager.Current.CharacterFilter.LoginComplete += new EventHandler(LoginComplete);
                Core.ChatBoxMessage += CheckForKillTaskMessage;
                CoreManager.Current.CommandLineText += new EventHandler<ChatParserInterceptEventArgs>(CheckForTaskListQuery);
            }
            catch (Exception ex) {  }
        }

        void LoginComplete(object sender, EventArgs e)
        {
            Log("KillTaskTracker enabled. Type '/tasks' to list each task and its progress.");

            if (startup_exception_ != null)
            {
                Log(startup_exception_);
            }

            try
            {
                progress_filename_ = dll_directory_ + "\\" + Core.CharacterFilter.Name + ".json";
                LoadProgress();
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        Boolean LoadProgress()
        {
            if (File.Exists(progress_filename_))
            {
                using (StreamReader r = new StreamReader(progress_filename_))
                {
                    string json = r.ReadToEnd();
                    foreach (KeyValuePair<String, int> progress in JsonConvert.DeserializeObject<List<KeyValuePair<String, int>>>(json))
                    {
                        foreach (List<KillTask> task_list in start_messages_.Values) {
                            foreach (KillTask task in task_list) {
                                if (task.name == progress.Key) {
                                    task.in_progress = true;
                                    task.count = progress.Value;
                                    active_tasks_.Add(task);
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        void CheckForKillTaskMessage(object sender, ChatTextInterceptEventArgs e)
        {
            try
            {
                // A color of 3 implies a private tell.
                if (e.Color == 3)
                {
                    // Check if tell starts a kill task.
                    if (start_messages_.ContainsKey(e.Text))
                    {
                        foreach (KillTask task in start_messages_[e.Text])
                        {
                            if (!task.in_progress)
                            {
                                task.in_progress = true;
                                task.count = 0;
                                active_tasks_.Add(task);
                                Log("Started task: " + task.name);
                                OverwriteProgressFile();
                            }
                        }
                    }

                    // Check if tell ends a kill task.
                    if (end_messages_.ContainsKey(e.Text))
                    {
                        KillTask task = end_messages_[e.Text];
                        task.in_progress = false;
                        task.count = 0;
                        active_tasks_.Remove(task);
                        Log("Finished task: " + task.name);
                        OverwriteProgressFile();
                    }
                }

                // A color of is for any, can we be more specific?
                if (e.Color == 0)
                {
                    // If this message is a kill task message.
                    Match match = kill_regex_.Match(e.Text);
                    if (match.Success)
                    {
                        int count = Convert.ToInt32(match.Groups[1].Value);
                        String monster_substring = match.Groups[2].Value;
                        if (monster_substrings_.ContainsKey(monster_substring))
                        {
                            KillTask task = monster_substrings_[monster_substring];
                            if (!task.in_progress)
                            {
                                task.in_progress = true;
                                task.count = count;
                                active_tasks_.Add(task);
                                Log("Implicitly started task: " + task.name);
                            }
                            else
                            {
                                task.count = count;
                            }
                        }
                        OverwriteProgressFile();
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        void OverwriteProgressFile()
        {
            List<KeyValuePair<String, int>> output_tasks = new List<KeyValuePair<String, int>>();
            foreach (KillTask task in active_tasks_)
            {
                output_tasks.Add(new KeyValuePair<String, int>(task.name, task.count));
            }
            File.WriteAllText(progress_filename_, JsonConvert.SerializeObject(output_tasks));
        }

        void CheckForTaskListQuery(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                if (e.Text == null)
                {
                    return;
                }

                if (e.Text == "/tasks")
                {
                    foreach (KillTask task in active_tasks_)
                    {
                        Log(task.name + " - " + task.count + " of " + task.num_kills + ".");
                    }
                    e.Eat = true;
                }
            }
            catch (Exception ex) 
            { 
                Log(ex.ToString());
            }
        }

        /// <summary>
        /// This is called when the plugin is shut down. This happens only once.
        /// </summary>
        protected override void Shutdown()
        {
            try
            {
                active_tasks_.Clear();
            }
            catch (Exception ex) {  }
        }

        void Log(String message)
        {
            CoreManager.Current.Actions.AddChatText(message, 8);
        }
    }

    public class KillTask
    {
        // Data from the definition of this task.
        public String name { get; set; }
        public String start_message { get; set; }
        public List<String> monster_substrings { get; set; }
        public List<String> end_messages { get; set; }
        public int num_kills { get; set; }

        // Data about the progress of the task.
        public Boolean in_progress { get; set; }
        public int count { get; set; }
    }
}
