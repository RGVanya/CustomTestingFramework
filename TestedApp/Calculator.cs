namespace TestedApp
{
    public class AuthService
    {
        public bool Login(string user, string pass) => user == "admin" && pass == "123";

        
        public async Task<string> GetTokenAsync()
        {
            await Task.Delay(50);
            return "TOKEN-XYZ";
        }

        public int CalculateDiskSpace(int files, int size)
        {
            if (files < 0) throw new ArgumentException("Negative files");
            return files * size;
        }
    }
}
