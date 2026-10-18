using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Moonbird
{
    [Serializable]
    public sealed class RecoverySession
    {
        public MapDocument document;
        public string filePath;
        public string savedState;
    }

    public static class MapStore
    {
        internal static string StorageRoot;
        public static string RecoveryPath => Path.Combine(StorageRoot ?? Application.persistentDataPath, "Recovery", "session.json");

        public static MapDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4_000_000)
                throw new InvalidDataException("비어 있거나 너무 큰 맵 파일이에요.");
            MapDocument map;
            try { map = JsonUtility.FromJson<MapDocument>(json); }
            catch (Exception) { throw new InvalidDataException("JSON 형식을 읽을 수 없어요."); }
            if (map == null) throw new InvalidDataException("맵 데이터가 없어요.");
            if (!map.IsValid(out var error)) throw new InvalidDataException(error);
            return map;
        }

        public static MapDocument Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

        public static void Save(string path, MapDocument map)
        {
            if (!map.IsValid(out var error)) throw new InvalidDataException(error);
            AtomicWrite(path, JsonUtility.ToJson(map, true));
        }

        public static void Recover(MapDocument map, string path, string savedState)
        {
            AtomicWrite(RecoveryPath, JsonUtility.ToJson(new RecoverySession
                { document = map, filePath = path, savedState = savedState }, true));
        }

        public static RecoverySession LoadRecovery()
        {
            if (!File.Exists(RecoveryPath)) return null;
            try
            {
                var session = JsonUtility.FromJson<RecoverySession>(File.ReadAllText(RecoveryPath, Encoding.UTF8));
                return session?.document != null && session.document.IsValid(out _) ? session : null;
            }
            catch (Exception e) { Debug.LogWarning("복구본을 읽지 못했어요: " + e.Message); return null; }
        }

        static void AtomicWrite(string path, string contents)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".moonbird-tmp";
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    // Windows-only native file picker, with no third-party runtime plugin.
    public static class WindowsFileDialog
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Blittable layout: avoid Mono recursively marshaling StringBuilder fields.
        [StructLayout(LayoutKind.Sequential)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr owner, instance;
            public IntPtr filter, customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDirectory, title;
            public int flags;
            public short fileOffset, fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData, hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reserved2, flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetOpenFileNameW(ref OpenFileName dialog);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetSaveFileNameW(ref OpenFileName dialog);
        [DllImport("comdlg32.dll")] static extern int CommDlgExtendedError();
#endif

        public static string Choose(bool save, string currentPath = null)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Shell dialogs need an STA thread. Keep them off Unity's small native main stack.
            string result = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { result = ChooseOnStaThread(save, currentPath, IntPtr.Zero); }
                catch (Exception e) { failure = e; }
            }, 8 * 1024 * 1024) { IsBackground = true, Name = "Moonbird file dialog" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new IOException("파일 선택 창을 열지 못했어요. " + failure.Message, failure);
            return result;
#else
            throw new PlatformNotSupportedException("파일 선택은 Windows에서 지원해요.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static string ChooseOnStaThread(bool save, string currentPath, IntPtr owner)
        {
            var initial = string.IsNullOrEmpty(currentPath) ? "moonbird-map.json" : Path.GetFileName(currentPath);
            var fileBuffer = Marshal.AllocHGlobal(4096 * 2);
            var filter = Marshal.StringToHGlobalUni("Moonbird 맵 (*.json)\0*.json\0모든 파일 (*.*)\0*.*\0\0");
            var directory = Marshal.StringToHGlobalUni(!string.IsNullOrEmpty(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath))
                ? Path.GetDirectoryName(currentPath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            var title = Marshal.StringToHGlobalUni(save ? "달을 꿈꾸는 새 — 맵 저장" : "달을 꿈꾸는 새 — 맵 열기");
            var extension = Marshal.StringToHGlobalUni("json");
            try
            {
                var bytes = new byte[4096 * 2];
                if (save) Encoding.Unicode.GetBytes(initial, 0, initial.Length, bytes, 0);
                Marshal.Copy(bytes, 0, fileBuffer, bytes.Length);
                var dialog = new OpenFileName
                {
                    structSize = Marshal.SizeOf<OpenFileName>(), owner = owner,
                    filter = filter, filterIndex = 1, file = fileBuffer, maxFile = 4096,
                    initialDirectory = directory, title = title, defaultExtension = extension,
                    flags = 0x80000 | 0x800 | 0x8 | (save ? 0x2 : 0x1000)
                };
                if (save ? GetSaveFileNameW(ref dialog) : GetOpenFileNameW(ref dialog)) return Marshal.PtrToStringUni(fileBuffer);
                if (CommDlgExtendedError() != 0) throw new IOException("파일 선택 창을 열 수 없어요.");
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer); Marshal.FreeHGlobal(filter); Marshal.FreeHGlobal(directory);
                Marshal.FreeHGlobal(title); Marshal.FreeHGlobal(extension);
            }
        }
#endif
    }
}
