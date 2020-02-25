using System;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Dalion.HttpMessageSigning.Signing {
    public class DigestSignatureHeaderEnsurerTests {
        private readonly IHashAlgorithmFactory _hashAlgorithmFactory;
        private readonly IBase64Converter _base64Converter;
        private readonly DigestSignatureHeaderEnsurer _sut;

        public DigestSignatureHeaderEnsurerTests() {
            FakeFactory.Create(out _hashAlgorithmFactory, out _base64Converter);
            _sut = new DigestSignatureHeaderEnsurer(_hashAlgorithmFactory, _base64Converter);
        }

        public class EnsureHeader : DigestSignatureHeaderEnsurerTests {
            private readonly DateTimeOffset _timeOfSigning;
            private readonly HttpRequestMessage _httpRequest;
            private readonly SigningSettings _settings;

            public EnsureHeader() {
                _timeOfSigning = new DateTimeOffset(2020, 2, 24, 11, 20, 14, TimeSpan.FromHours(1));
                _httpRequest = new HttpRequestMessage {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("http://dalion.eu/api/resource/id1")
                };
                _settings = new SigningSettings {
                    Expires = TimeSpan.FromMinutes(5),
                    ClientKey = new ClientKey {
                        Id = new KeyId("client1"),
                        Secret = new Secret("s3cr3t")
                    },
                    Headers = new[] {
                        HeaderName.PredefinedHeaderNames.RequestTarget,
                        HeaderName.PredefinedHeaderNames.Date,
                        HeaderName.PredefinedHeaderNames.Expires,
                        new HeaderName("dalion_app_id")
                    },
                    DigestHashAlgorithm = HashAlgorithm.SHA256
                };
            }

            [Fact]
            public void GivenNullRequest_ThrowsArgumentNullException() {
                Func<Task> act = () => _sut.EnsureHeader(null, _settings, _timeOfSigning);
                act.Should().Throw<ArgumentNullException>();
            }

            [Fact]
            public void GivenNullSettings_ThrowsArgumentNullException() {
                Func<Task> act = () => _sut.EnsureHeader(_httpRequest, null, _timeOfSigning);
                act.Should().Throw<ArgumentNullException>();
            }

            [Theory]
            [InlineData("GET")]
            [InlineData("TRACE")]
            [InlineData("HEAD")]
            [InlineData("DELETE")]
            public async Task WhenMethodDoesNotHaveBody_DoesNotSetDigestHeader(string method) {
                _httpRequest.Method = new HttpMethod(method);

                await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                _httpRequest.Headers.Should().NotContain("Digest");
            }

            [Fact]
            public async Task WhenDigestIsDisabled_DoesNotSetDigestHeader() {
                _settings.DigestHashAlgorithm = HashAlgorithm.None;

                await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                _httpRequest.Headers.Should().NotContain("Digest");
            }

            [Fact]
            public async Task WhenDigestIsAlreadyPresent_DoesNotChangeDigestHeader() {
                _httpRequest.Headers.Add("Digest", "SHA-256=abc123");

                await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                _httpRequest.Headers.Should().Contain(h => h.Key == "Digest" && h.Value == new StringValues("SHA-256=abc123"));
            }
            
            [Fact]
            public async Task WhenDigestIsAlreadyPresent_ButIncorrectlyCased_DoesNotChangeDigestHeader() {
                _httpRequest.Headers.Add("digest", "SHA-256=abc123");

                await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                _httpRequest.Headers.Should().Contain(h => h.Key == "digest" && h.Value == new StringValues("SHA-256=abc123"));
                _httpRequest.Headers.Should().NotContain("Digest");
            }

            [Fact]
            public async Task WhenRequestHasNoContent_SetsEmptyDigestHeader() {
                _httpRequest.Content = null;

                await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                _httpRequest.Headers.Should().NotContain("Digest");
            }

            [Fact]
            public async Task ReturnsExpectedString() {
                _httpRequest.Content = new StringContent("abc123", Encoding.UTF8, MediaTypeNames.Application.Json);

                using (var hashAlgorithm = A.Fake<IHashAlgorithm>()) {
                    A.CallTo(() => _hashAlgorithmFactory.Create(_settings.DigestHashAlgorithm))
                        .Returns(hashAlgorithm);

                    var expectedBodyBytes = Encoding.UTF8.GetBytes("abc123");
                    var hashBytes = new byte[] {0x01, 0x02};
                    A.CallTo(() => hashAlgorithm.ComputeHash(A<byte[]>.That.IsSameSequenceAs(expectedBodyBytes)))
                        .Returns(hashBytes);

                    A.CallTo(() => hashAlgorithm.Name)
                        .Returns("SHA-384");

                    var base64 = "xyz==";
                    A.CallTo(() => _base64Converter.ToBase64(hashBytes))
                        .Returns(base64);

                    await _sut.EnsureHeader(_httpRequest, _settings, _timeOfSigning);

                    _httpRequest.Headers.Should().Contain(h => h.Key == "Digest" && h.Value == new StringValues("SHA-384=xyz=="));
                }
            }
        }
    }
}