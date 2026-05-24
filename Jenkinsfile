node {
    stage('Clean Old Files') {
        sh 'ssh -o StrictHostKeyChecking=no pi@192.168.1.192 "mkdir -p ~/blazor-app && rm -rf ~/blazor-app/*"'
    }

    stage('Deploy Code to Pi') {
        sh 'scp -o StrictHostKeyChecking=no -r /workspace/blazor-app/* pi@192.168.1.192:~/blazor-app/'
    }

    stage('Build') {
        sh '''ssh -o StrictHostKeyChecking=no pi@192.168.1.192 "
            cd ~/blazor-app &&
            docker build --target build -t tiktoktracker-build -f TikTokTracker.Web/Dockerfile .
        "'''
    }

    stage('Test') {
        sh '''ssh -o StrictHostKeyChecking=no pi@192.168.1.192 "
            cd ~/blazor-app &&
            docker run --rm tiktoktracker-build \
                dotnet test /src/TikTokTracker.Tests/TikTokTracker.Tests.csproj \
                -c Release --no-restore --logger 'console;verbosity=normal'
        "'''
    }

    stage('Publish & Run') {
        sh 'ssh -o StrictHostKeyChecking=no pi@192.168.1.192 "cd ~/blazor-app && docker compose down && docker compose up -d --build"'
    }
}
