﻿//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2010-2013 Garrett Serack and CoApp Contributors. 
//     Contributors can be discovered using the 'git log' command.
//     All rights reserved.
// </copyright>
// <license>
//     The software is licensed under the Apache 2.0 License (the "License")
//     You may not use the software except in compliance with the License. 
// </license>
//-----------------------------------------------------------------------

namespace ClrPlus.Platform.Process {
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ClrPlus.Windows.Api;
    using Core.Collections;
    using System.Linq;
    using Core.Extensions;

    public class Executable {

        public Executable(string filename, params string[] basePath) {
            
            // check for it in the current directory somewhere first, as well as in the PATH.
            Path = FindHighestBinary(filename, Environment.CurrentDirectory, true, true);

            // then check all the places they could have said it might be.
            foreach (var bp in basePath) {
                Path = FindHighestBinary(filename, bp, true, false,Path);
            }
            
            if (string.IsNullOrEmpty(Path)) {
                throw new Exception("Unable to find executable '{0}'".format(filename));
            }
        }

        public string Path;

        private static char[] _delimiter = new[] {
            ';'
        };


        private UInt64 FileVersion(string fullPath) {
            try {
                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)) {
                    var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
                    return (ulong)((ulong)versionInfo.FileMajorPart << 48) | (ulong)((ulong)versionInfo.FileMinorPart << 32) | (ulong)((ulong)versionInfo.FileBuildPart << 16) | (ulong)((ulong)versionInfo.FilePrivatePart);
                }
            }
            catch {
            }
            return 0;
        }

        private string FindHighestBinary(string binaryName, string baseFolder, bool recursive = false, bool searchPathFirst = false, string currentLeader = null) {
            try {
                if (string.IsNullOrEmpty(binaryName) || string.IsNullOrEmpty(baseFolder)) {
                    return currentLeader;
                }

                if (searchPathFirst) {
                    var path = Environment.GetEnvironmentVariable("PATH");
                    if (!string.IsNullOrEmpty(path)) {
                        foreach (var folder in path.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries)) {
                            currentLeader = FindHighestBinary(binaryName, folder, false, false, currentLeader);
                        }
                    }
                }

                if (Directory.Exists(baseFolder)) {
                    var fullpath = System.IO.Path.Combine(baseFolder, binaryName);
                    if (FileVersion(currentLeader) < FileVersion(fullpath)) {
                        currentLeader = fullpath;
                    }
                    if (recursive) {
                        foreach (var folder in Directory.EnumerateDirectories(baseFolder)) {
                            currentLeader = FindHighestBinary(binaryName, folder, true, false, currentLeader);
                        }
                    }
                }
            }
            catch {
            }
            return currentLeader;
        }

        public AsyncProcess Exec() {
            return AsyncProcess.Start(Path);
        }

        public AsyncProcess Exec(string cmdline) {
            return AsyncProcess.Start(Path, cmdline);
        }

        public AsyncProcess Exec(IDictionary environment) {
            return AsyncProcess.Start(Path, environment);
        }

        public AsyncProcess Exec(string cmdline, IDictionary environment) {
            return AsyncProcess.Start(Path, cmdline, environment);
        }

        public AsyncProcess Exec(string cmdline, string workingDirectory, IDictionary environment) {
            return AsyncProcess.Start(new ProcessStartInfo {
                FileName = Path,
                WorkingDirectory = workingDirectory,
            },environment);
        }

        public AsyncProcess Exec(string cmdline, string workingDirectory) {
            return AsyncProcess.Start(new ProcessStartInfo {
                FileName = Path,
                WorkingDirectory = workingDirectory,
            });
        }
    }


    public class AsyncProcess {
        protected Process _process;
        private AsynchronouslyEnumerableList<string> _stdError = new AsynchronouslyEnumerableList<string>();
        private AsynchronouslyEnumerableList<string> _stdOut = new AsynchronouslyEnumerableList<string>();

        protected AsyncProcess(Process process) {
            _process = process;
        }

        public static AsyncProcess Start(ProcessStartInfo startInfo) {
            return Start(startInfo, null);
        }

        public static AsyncProcess Start(ProcessStartInfo startInfo, IDictionary environment) {
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;

            if (environment != null) {
                foreach (var i in environment.Keys) {
                    startInfo.EnvironmentVariables[(string)i] = (string)environment[i];
                }
            }
            
            var result = new  AsyncProcess(new Process {
                StartInfo = startInfo
            });

            result._process.EnableRaisingEvents = true;

            // set up std* access
            result._process.ErrorDataReceived += (sender, args) => {
                result._stdError.Add(args.Data ?? string.Empty);
            };

            result._process.OutputDataReceived += (sender, args) => {
                result._stdOut.Add(args.Data ?? string.Empty);
            };
            result._process.Exited +=(sender, args) => {
                result.WaitForExit();
                result._stdError.Completed();
                result._stdOut.Completed();
            };
            
            result._process.Start();
            result._process.BeginErrorReadLine();
            result._process.BeginOutputReadLine();
            

            return result;
        }
            
        public IEnumerable<string> StandardError {
            get {
                return _stdError;
            }
        }

        public IEnumerable<string> StandardOutput {
            get {
                return _stdOut;     
            }
        }
#if BAD_IDEA
        public static AsyncProcess Start(System.Diagnostics.ProcessStartInfo startInfo) {
            return Start(new ProcessStartInfo(startInfo));
        }

#endif

        public static AsyncProcess Start(string fileName) {
            return Start(new ProcessStartInfo {
                FileName = fileName
            });
        }

        public static AsyncProcess Start(string fileName, IDictionary environment ) {
            return Start(new ProcessStartInfo {
                FileName = fileName
            }, environment);
        }

        public static AsyncProcess Start(string fileName, string parameters, IDictionary environment) {
            return Start(new ProcessStartInfo {
                FileName = fileName,
                Arguments = parameters
            }, environment);
        }
        public static AsyncProcess Start(string fileName, string parameters) {
            return Start(new ProcessStartInfo {
                FileName = fileName,
                Arguments = parameters
            });
        }
        

        public void Kill() {
            _process.Kill();
        }

        public bool WaitForExit(int milliseconds) {
            return _process.WaitForExit(milliseconds);
        }

        public void WaitForExit() {
            WaitForExit(-1);
        }

        public bool WaitForInputIdle(int milliseconds) {
            return _process.WaitForInputIdle(milliseconds);
        }

        public bool WaitForInputIdle() {
            return WaitForInputIdle(-1);
        }

        public int ExitCode {
            get {
                return _process.ExitCode;
            }
        }

        public bool HasExited {
            get {
                return _process.HasExited;
            }
        }

        public DateTime ExitTime {
            get {
                return _process.ExitTime;
            }
        }

        public IntPtr Handle {
            get {
                return _process.Handle;
            }
        }

        public int HandleCount {
            get {
                return _process.HandleCount;
            }
        }

        public int Id {
            get {
                return _process.Id;
            }
        }

        public string MachineName {
            get {
                return _process.MachineName;
            }
        }

        public IntPtr MainWindowHandle {
            get {
                return _process.MainWindowHandle;
            }
        }

        public string MainWindowTitle {
            get {
                return _process.MainWindowTitle;
            }
        }

        public ProcessModule MainModule {
            get {
                return _process.MainModule;
            }
        }

        public IntPtr MaxWorkingSet {
            get {
                return _process.MaxWorkingSet;
            }
        }

        public IntPtr MinWorkingSet {
            get {
                return _process.MinWorkingSet;
            }
        }

        public ProcessModuleCollection Modules {
            get {
                return _process.Modules;
            }
        }

        public long NonpagedSystemMemorySize64 {
            get {
                return _process.NonpagedSystemMemorySize64;
            }
        }

        public long PagedMemorySize64 {
            get {
                return _process.PagedMemorySize64;
            }
        }

        public long PagedSystemMemorySize64 {
            get {
                return _process.PagedSystemMemorySize64;
            }
        }

        public long PeakPagedMemorySize64 {
            get {
                return _process.PeakPagedMemorySize64;
            }
        }

        public long PeakWorkingSet64 {
            get {
                return _process.PeakWorkingSet64;
            }
        }

        public long PeakVirtualMemorySize64 {
            get {
                return _process.PeakVirtualMemorySize64;
            }
        }

        public bool PriorityBoostEnabled {
            get {
                return _process.PriorityBoostEnabled;
            }
            set {
                _process.PriorityBoostEnabled = value;
            }
        }

        public ProcessPriorityClass PriorityClass{
            get {
                return _process.PriorityClass;
            }
            set {
                _process.PriorityClass = value;
            }
        }

        public long PrivateMemorySize64 {
            get {
                return _process.PrivateMemorySize64;
            }
        }

        public TimeSpan PrivilegedProcessorTime {
            get {
                return _process.PrivilegedProcessorTime;
            }
        }

        public string ProcessName {
            get {
                return _process.ProcessName;
            }
        }

        public IntPtr ProcessorAffinity {
            get {
                return _process.ProcessorAffinity;
            }
            set {
                _process.ProcessorAffinity = value;
            }
        }

        public bool Responding {
            get {
                return _process.Responding;
            }
        }

        public int SessionId {
            get {
                return _process.SessionId;
            }
        }

        
        public DateTime StartTime {
            get {
                return _process.StartTime;
            }
        }

        public ISynchronizeInvoke SynchronizingObject {
            get {
                return _process.SynchronizingObject;
            }
        }

        public ProcessThreadCollection Threads {
            get {
                return _process.Threads;
            }
        }

        public TimeSpan TotalProcessorTime {
            get {
                return _process.TotalProcessorTime;
            }
        }

        public TimeSpan UserProcessorTime {
            get {
                return _process.UserProcessorTime;
            }
        }

        public long VirtualMemorySize64 {
            get {
                return _process.VirtualMemorySize64;
            }
        }

        public bool EnableRaisingEvents {
            get {
                return _process.EnableRaisingEvents;
            }
            set {
                _process.EnableRaisingEvents = value;
            }
        }

        public long WorkingSet64 {
            get {
                return _process.WorkingSet64;
            }
        }

        public event EventHandler Exited {
            add {
                _process.Exited += value;
            }
            remove {
                _process.Exited -= value;
            }
        }

        public bool CloseMainWindow() {
            return _process.CloseMainWindow();
        }

        protected void Dispose(bool disposing) {
            if (_process != null) {
                _process.Dispose();
            }
            _process = null;
        }

        public void Close() {
            _process.Close();

        }

        public static void EnterDebugMode() {
            Process.EnterDebugMode();
        }

        public static void LeaveDebugMode() {
            Process.LeaveDebugMode();
        }

        public static AsyncProcess GetProcessById(int processId, string machineName) {
            return new AsyncProcess(Process.GetProcessById(processId, machineName));
        }

        public static AsyncProcess GetProcessById(int processId) {
            return new AsyncProcess(Process.GetProcessById(processId));
        }

        public static AsyncProcess[] GetProcessesByName(string processName) {
            return Process.GetProcessesByName(processName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcessesByName(string processName, string machineName) {
            return Process.GetProcessesByName(processName, machineName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcesses() {
            return Process.GetProcesses().Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcesses(string machineName) {
            return Process.GetProcesses(machineName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess GetCurrentProcess() {
            return new AsyncProcess(Process.GetCurrentProcess());
        }

        public void Refresh() {
            _process.Refresh();
        }

    }
}
