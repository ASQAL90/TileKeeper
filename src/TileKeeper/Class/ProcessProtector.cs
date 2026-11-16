using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TileKeeper.Class
{
    /// <summary>
    /// Modifies the current process's DACL (Discretionary Access Control List) to deny
    /// the PROCESS_TERMINATE right to all standard users, so that "End Task" in Windows
    /// Task Manager returns "Access Denied" without requiring Administrator privileges.
    ///
    /// <para>
    /// <b>Why a standard user can do this:</b> The process owner always holds the
    /// <c>WRITE_DAC</c> right on their own process object, regardless of elevation level.
    /// <c>WRITE_DAC</c> is all that is needed to modify the DACL.
    /// </para>
    ///
    /// <para>
    /// <b>What this does NOT protect against:</b>
    /// <list type="bullet">
    ///   <item>Administrators — <c>SeDebugPrivilege</c> bypasses DACL checks entirely.</item>
    ///   <item>The same user if they know how to call <c>SetSecurityInfo</c> to revert the DACL.</item>
    ///   <item>Machine reboot — the DACL lives only in the process object, not on disk.</item>
    /// </list>
    /// This is appropriate for kiosk/managed environments with non-technical end users.
    /// </para>
    /// </summary>
    internal static class ProcessProtector
    {
        // ── Win32 constants ───────────────────────────────────────────────────

        /// <summary>Kernel object type (a process is a kernel object).</summary>
        private const uint SE_KERNEL_OBJECT = 6;

        /// <summary>We are modifying only the DACL portion of the security descriptor.</summary>
        private const uint DACL_SECURITY_INFORMATION = 0x00000004;

        /// <summary>Access mask bit for TerminateProcess().</summary>
        private const uint PROCESS_TERMINATE = 0x0001;

        /// <summary>ACL structure revision used by all modern Windows versions.</summary>
        private const uint ACL_REVISION = 2;

        /// <summary>WellKnownSidType value for "Everyone" (World SID, S-1-1-0).</summary>
        private const int WIN_WORLD_SID = 1;

        /// <summary>AclSizeInformation enum value for GetAclInformation.</summary>
        private const uint ACL_SIZE_INFORMATION_CLASS = 2;

        // ── Structures ────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct ACL_SIZE_INFORMATION
        {
            public uint AceCount;
            public uint AclBytesInUse;
            public uint AclBytesFree;
        }

        // ── P/Invoke declarations ─────────────────────────────────────────────

        /// <summary>Returns the pseudo-handle for the current process. Always valid; never close it.</summary>
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>Reads the security information (including DACL) from a kernel object.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetSecurityInfo(
            IntPtr handle,
            uint objectType,
            uint securityInfo,
            out IntPtr pSidOwner,
            out IntPtr pSidGroup,
            out IntPtr pDacl,
            out IntPtr pSacl,
            out IntPtr pSecurityDescriptor);

        /// <summary>Writes new security information (including a modified DACL) to a kernel object.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint SetSecurityInfo(
            IntPtr handle,
            uint objectType,
            uint securityInfo,
            IntPtr pSidOwner,
            IntPtr pSidGroup,
            IntPtr pDacl,
            IntPtr pSacl);

        /// <summary>Returns size and count information about an ACL.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAclInformation(
            IntPtr pAcl,
            ref ACL_SIZE_INFORMATION pAclInformation,
            uint nAclInformationLength,
            uint dwAclInformationClass);

        /// <summary>Returns a pointer to an ACE at a given index within an ACL.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAce(
            IntPtr pAcl,
            uint dwAceIndex,
            out IntPtr pAce);

        /// <summary>Creates an empty, initialised ACL buffer of the specified size.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool InitializeAcl(
            IntPtr pAcl,
            uint nAclLength,
            uint dwAclRevision);

        /// <summary>Appends an Access-Denied ACE to an ACL.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAccessDeniedAce(
            IntPtr pAcl,
            uint dwAceRevision,
            uint accessMask,
            IntPtr pSid);

        /// <summary>Copies one or more ACEs from a source buffer into an ACL.</summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAce(
            IntPtr pAcl,
            uint dwAceRevision,
            uint dwStartingAceIndex,
            IntPtr pAceList,
            uint nAceListLength);

        /// <summary>
        /// Allocates and fills a well-known SID (e.g. "Everyone").
        /// Call first with <paramref name="pSid"/> = IntPtr.Zero and <paramref name="cbSid"/> = 0
        /// to discover the required buffer size, then call again with an allocated buffer.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CreateWellKnownSid(
            int wellKnownSidType,
            IntPtr domainSid,
            IntPtr pSid,
            ref uint cbSid);

        /// <summary>Frees a memory block allocated by the security API (e.g. the security descriptor from GetSecurityInfo).</summary>
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Modifies the current process's DACL to prepend a DENY ACE that blocks
        /// <c>PROCESS_TERMINATE</c> for the "Everyone" SID (S-1-1-0).
        ///
        /// The deny ACE is placed before all existing allow ACEs so that Windows
        /// evaluates it first. After this call, any process that calls
        /// <c>OpenProcess(PROCESS_TERMINATE, ...)</c> on this process receives
        /// ERROR_ACCESS_DENIED unless it holds <c>SeDebugPrivilege</c> (Administrators).
        ///
        /// Silently logs and returns if anything fails — the app continues normally
        /// with its default security descriptor.
        /// </summary>
        public static void DenyTerminateForStandardUsers()
        {
            IntPtr pSecurityDescriptor = IntPtr.Zero;
            IntPtr pNewAcl             = IntPtr.Zero;
            IntPtr pEveryoneSid        = IntPtr.Zero;

            try
            {
                IntPtr hProcess = GetCurrentProcess(); // pseudo-handle, always valid

                // ── Step 1: Read current DACL from the process object ─────────
                uint err = GetSecurityInfo(
                    hProcess,
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    out _,    // owner SID — not needed
                    out _,    // group SID — not needed
                    out IntPtr pOldDacl,
                    out _,    // SACL — not needed
                    out pSecurityDescriptor);

                if (err != 0)
                {
                    Logger.Log($"ProcessProtector: GetSecurityInfo failed (error {err})");
                    return;
                }

                // ── Step 2: Build "Everyone" SID ─────────────────────────────
                uint sidSize = 0;
                CreateWellKnownSid(WIN_WORLD_SID, IntPtr.Zero, IntPtr.Zero, ref sidSize);
                pEveryoneSid = Marshal.AllocHGlobal((int)sidSize);

                if (!CreateWellKnownSid(WIN_WORLD_SID, IntPtr.Zero, pEveryoneSid, ref sidSize))
                {
                    Logger.Log($"ProcessProtector: CreateWellKnownSid failed (error {Marshal.GetLastWin32Error()})");
                    return;
                }

                // ── Step 3: Measure the existing ACL ─────────────────────────
                var aclInfo = new ACL_SIZE_INFORMATION();
                GetAclInformation(
                    pOldDacl,
                    ref aclInfo,
                    (uint)Marshal.SizeOf(aclInfo),
                    ACL_SIZE_INFORMATION_CLASS);

                // ── Step 4: Allocate a new ACL large enough for the deny ACE + all existing ACEs ──
                // An ACCESS_DENIED_ACE header is 8 bytes; the SID is appended after the header.
                uint denyAceSize  = 8 + sidSize;
                uint newAclSize   = aclInfo.AclBytesInUse + denyAceSize + 16; // 16 bytes extra padding
                pNewAcl = Marshal.AllocHGlobal((int)newAclSize);
                InitializeAcl(pNewAcl, newAclSize, ACL_REVISION);

                // ── Step 5: Prepend the DENY ACE (must come before any ALLOW ACEs) ─
                if (!AddAccessDeniedAce(pNewAcl, ACL_REVISION, PROCESS_TERMINATE, pEveryoneSid))
                {
                    Logger.Log($"ProcessProtector: AddAccessDeniedAce failed (error {Marshal.GetLastWin32Error()})");
                    return;
                }

                // ── Step 6: Copy all existing ACEs into the new ACL ───────────
                for (uint i = 0; i < aclInfo.AceCount; i++)
                {
                    GetAce(pOldDacl, i, out IntPtr pAce);

                    // ACE_HEADER layout: AceType (1 byte), AceFlags (1 byte), AceSize (2 bytes)
                    ushort aceSize = (ushort)Marshal.ReadInt16(pAce, 2);
                    AddAce(pNewAcl, ACL_REVISION, uint.MaxValue, pAce, aceSize);
                }

                // ── Step 7: Apply the new DACL to the process ─────────────────
                err = SetSecurityInfo(
                    hProcess,
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    pNewAcl,
                    IntPtr.Zero);

                if (err != 0)
                    Logger.Log($"ProcessProtector: SetSecurityInfo failed (error {err})");
                else
                    Logger.Log("ProcessProtector: PROCESS_TERMINATE denied for standard users.");
            }
            catch (Exception ex)
            {
                Logger.Log($"ProcessProtector.DenyTerminateForStandardUsers failed: {ex.Message}");
            }
            finally
            {
                // pSecurityDescriptor was allocated by GetSecurityInfo → free with LocalFree
                if (pSecurityDescriptor != IntPtr.Zero) LocalFree(pSecurityDescriptor);

                // pNewAcl and pEveryoneSid were allocated by us → free with Marshal.FreeHGlobal
                if (pNewAcl      != IntPtr.Zero) Marshal.FreeHGlobal(pNewAcl);
                if (pEveryoneSid != IntPtr.Zero) Marshal.FreeHGlobal(pEveryoneSid);
            }
        }
    }
}
