namespace SavienPlayer.Core;

public enum PlaybackErrorKind
{
    FileNotFound,
    AccessDenied,
    InputOutput,
    UnsupportedCodec,
    Decoding,
    Unknown
}

public sealed class PlaybackError : EventArgs
{
    public PlaybackErrorKind Kind { get; }
    public string Message => Kind switch
    {
        PlaybackErrorKind.FileNotFound => "동영상 파일을 찾을 수 없습니다. 파일이 이동·삭제되었는지 확인한 뒤 다시 열어주세요.",
        PlaybackErrorKind.AccessDenied => "파일을 읽을 권한이 없습니다. 파일·폴더의 읽기 권한을 확인하거나 접근 가능한 폴더로 복사해주세요.",
        PlaybackErrorKind.InputOutput => "파일을 읽는 중 입출력 오류가 발생했습니다. 다른 프로그램의 파일 사용 여부와 저장 장치 연결을 확인해주세요.",
        PlaybackErrorKind.UnsupportedCodec => "이 영상의 코덱을 처리할 디코더를 찾을 수 없습니다. VLC를 업데이트하거나 다른 형식으로 변환해주세요.",
        PlaybackErrorKind.Decoding => "영상 데이터를 해석할 수 없습니다. 파일이 비어 있거나 손상되었을 수 있으니 다시 복사·다운로드해주세요.",
        _ => "영상을 재생하지 못했습니다. 다시 시도하거나 다른 파일을 열어주세요. 문제가 계속되면 진단 로그를 확인해주세요."
    };

    public PlaybackError(PlaybackErrorKind kind) => Kind = kind;
}
