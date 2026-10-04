namespace IdempotencyKit;

public interface IIdempotencyFingerprintGenerator
{
    string Generate(string method, string path, byte[] body);
}
