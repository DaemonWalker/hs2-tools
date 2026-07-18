import React, { useState, useCallback } from 'react'
import { 
  Card, 
  Button, 
  Input, 
  List, 
  Tag, 
  Space, 
  Typography, 
  Progress, 
  Empty,
  Alert,
  Tooltip,
  Badge,
  Statistic,
  Row,
  Col
} from 'antd'
import { 
  FolderOpenOutlined, 
  FileSearchOutlined, 
  CheckCircleOutlined, 
  PlayCircleOutlined,
  ReloadOutlined,
  InboxOutlined,
  UserOutlined,
  FolderAddOutlined,
  InfoCircleOutlined,
  DeleteOutlined,
  EditOutlined,
  CheckOutlined,
  CloseOutlined
} from '@ant-design/icons'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { pathJoin, getFileName } from '@renderer/logic/ioUtils'

const { TextArea } = Input
const { Text } = Typography

const { getAllFiles, readAllCharaNames, checkTargetDir, moveFile } = ipcUtils

interface OrganizeResult {
  scenePath: string
  sceneName: string
  matchedChars: string[]
  targetFolder: string
}

interface OrganizeTask {
  id: string
  charNames: string[]
  folderName: string
  results: OrganizeResult[]
  status: 'pending' | 'scanning' | 'analyzing' | 'moving' | 'completed' | 'error'
  progress: number
  totalScenes: number
  processedScenes: number
  error?: string
}

interface SceneOrganizerProps {
  onOrganizeComplete?: () => void
}

export const SceneOrganizer: React.FC<SceneOrganizerProps> = ({ onOrganizeComplete }) => {
  const { scenePath } = useSettingStore()
  const [tasks, setTasks] = useState<OrganizeTask[]>([])
  const [currentTaskId, setCurrentTaskId] = useState<string | null>(null)
  const [charInput, setCharInput] = useState('')
  const [folderInput, setFolderInput] = useState('')
  const [isProcessing, setIsProcessing] = useState(false)
  const [editingTask, setEditingTask] = useState<string | null>(null)
  const [editFolderName, setEditFolderName] = useState('')

  // 生成唯一ID
  const generateId = () => `task_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`

  // 添加新任务
  const handleAddTask = useCallback(() => {
    const names = charInput.split('\n').map(n => n.trim()).filter(Boolean)
    const folder = folderInput.trim()
    
    if (names.length === 0 || !folder) return

    const newTask: OrganizeTask = {
      id: generateId(),
      charNames: names,
      folderName: folder,
      results: [],
      status: 'pending',
      progress: 0,
      totalScenes: 0,
      processedScenes: 0
    }

    setTasks(prev => [...prev, newTask])
    setCharInput('')
    setFolderInput('')
  }, [charInput, folderInput])

  // 删除任务
  const handleRemoveTask = useCallback((taskId: string) => {
    setTasks(prev => prev.filter(t => t.id !== taskId))
    if (currentTaskId === taskId) {
      setCurrentTaskId(null)
    }
  }, [currentTaskId])

  // 开始编辑任务
  const handleStartEdit = useCallback((task: OrganizeTask) => {
    setEditingTask(task.id)
    setEditFolderName(task.folderName)
  }, [])

  // 保存编辑
  const handleSaveEdit = useCallback((taskId: string) => {
    if (!editFolderName.trim()) return
    
    setTasks(prev => prev.map(t => 
      t.id === taskId ? { ...t, folderName: editFolderName.trim() } : t
    ))
    setEditingTask(null)
    setEditFolderName('')
  }, [editFolderName])

  // 取消编辑
  const handleCancelEdit = useCallback(() => {
    setEditingTask(null)
    setEditFolderName('')
  }, [])

  // 执行单个任务
  const executeTask = useCallback(async (task: OrganizeTask) => {
    if (!scenePath()) return

    setCurrentTaskId(task.id)
    setIsProcessing(true)

    try {
      // 更新状态：扫描中
      setTasks(prev => prev.map(t => 
        t.id === task.id ? { ...t, status: 'scanning' } : t
      ))

      const targetDir = pathJoin(scenePath(), `hs_tools_${task.folderName}`)
      await checkTargetDir(targetDir)

      // 获取所有场景文件
      const scenes = await getAllFiles(scenePath(), { excludeDir: ['hs_tools_'] })
      
      setTasks(prev => prev.map(t => 
        t.id === task.id ? { 
          ...t, 
          status: 'analyzing', 
          totalScenes: scenes.length 
        } : t
      ))

      const results: OrganizeResult[] = []

      // 分析每个场景
      for (let i = 0; i < scenes.length; i++) {
        const scene = scenes[i]
        const sceneName = getFileName(scene)
        
        try {
          const names = await readAllCharaNames(scene)
          if (!names || names.length === 0) continue

          const matchedChars: string[] = []
          for (const targetName of task.charNames) {
            if (names.find(n => n.includes(targetName))) {
              matchedChars.push(targetName)
            }
          }

          if (matchedChars.length > 0) {
            results.push({
              scenePath: scene,
              sceneName,
              matchedChars,
              targetFolder: targetDir
            })
          }
        } catch (e) {
          console.error(`Error analyzing scene ${scene}:`, e)
        }

        // 更新进度
        if ((i + 1) % 5 === 0 || i === scenes.length - 1) {
          setTasks(prev => prev.map(t => 
            t.id === task.id ? { 
              ...t, 
              processedScenes: i + 1,
              progress: Math.round(((i + 1) / scenes.length) * 50)
            } : t
          ))
        }
      }

      // 更新状态：移动中
      setTasks(prev => prev.map(t => 
        t.id === task.id ? { 
          ...t, 
          status: 'moving',
          results,
          progress: 50
        } : t
      ))

      // 执行移动
      for (let i = 0; i < results.length; i++) {
        const result = results[i]
        try {
          await moveFile(result.scenePath, result.targetFolder)
        } catch (e) {
          console.error(`Error moving file ${result.scenePath}:`, e)
        }

        // 更新进度
        setTasks(prev => prev.map(t => 
          t.id === task.id ? { 
            ...t, 
            progress: 50 + Math.round(((i + 1) / results.length) * 50)
          } : t
        ))
      }

      // 完成
      setTasks(prev => prev.map(t => 
        t.id === task.id ? { 
          ...t, 
          status: 'completed',
          progress: 100
        } : t
      ))

    } catch (error) {
      setTasks(prev => prev.map(t => 
        t.id === task.id ? { 
          ...t, 
          status: 'error',
          error: error instanceof Error ? error.message : '未知错误'
        } : t
      ))
    } finally {
      setIsProcessing(false)
      setCurrentTaskId(null)
      onOrganizeComplete?.()
    }
  }, [scenePath, onOrganizeComplete])

  // 执行所有任务
  const handleExecuteAll = useCallback(async () => {
    const pendingTasks = tasks.filter(t => t.status === 'pending')
    for (const task of pendingTasks) {
      await executeTask(task)
    }
  }, [tasks, executeTask])

  // 获取状态颜色
  const getStatusColor = (status: OrganizeTask['status']) => {
    switch (status) {
      case 'completed': return 'success'
      case 'error': return 'error'
      case 'scanning':
      case 'analyzing':
      case 'moving': return 'processing'
      default: return 'default'
    }
  }

  // 获取状态文本
  const getStatusText = (status: OrganizeTask['status']) => {
    switch (status) {
      case 'pending': return '待执行'
      case 'scanning': return '扫描中'
      case 'analyzing': return '分析中'
      case 'moving': return '移动中'
      case 'completed': return '已完成'
      case 'error': return '失败'
      default: return '未知'
    }
  }

  if (!scenePath()) {
    return (
      <Empty
        image={<InboxOutlined style={{ fontSize: 64, color: 'var(--text-muted)' }} />}
        description="请先设置游戏路径"
      />
    )
  }

  return (
    <div className="scene-organizer">
      {/* 左侧：任务创建 */}
      <div className="organizer-sidebar">
        <Card 
          title={
            <Space>
              <FolderAddOutlined style={{ color: 'var(--theme-primary)' }} />
              <span>新建整理任务</span>
            </Space>
          }
          size="small"
          className="organizer-card"
        >
          <Space direction="vertical" style={{ width: '100%' }} size="middle">
            <div>
              <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
                <UserOutlined style={{ marginRight: 4 }} />
                角色名称（每行一个）
              </Text>
              <TextArea
                placeholder={`例如：
角色名称A
角色名称B
角色名称C`}
                value={charInput}
                onChange={(e) => setCharInput(e.target.value)}
                rows={5}
                disabled={isProcessing}
              />
            </div>

            <div>
              <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
                <FolderOpenOutlined style={{ marginRight: 4 }} />
                目标文件夹名称
              </Text>
              <Input
                placeholder="例如：我的收藏场景"
                value={folderInput}
                onChange={(e) => setFolderInput(e.target.value)}
                disabled={isProcessing}
                prefix={<InboxOutlined />}
                suffix={<Text type="secondary" style={{ fontSize: 12 }}>hs_tools_</Text>}
              />
            </div>

            <Button
              type="primary"
              icon={<CheckCircleOutlined />}
              onClick={handleAddTask}
              disabled={!charInput.trim() || !folderInput.trim() || isProcessing}
              block
            >
              添加到任务列表
            </Button>

            <Alert
              message="使用提示"
              description="系统会自动扫描所有场景文件，将包含指定角色的场景移动到目标文件夹中。"
              type="info"
              showIcon
              style={{ fontSize: 12 }}
            />
          </Space>
        </Card>

        {tasks.length > 0 && (
          <Card 
            title={
              <Space>
                <InfoCircleOutlined style={{ color: 'var(--theme-primary)' }} />
                <span>统计信息</span>
              </Space>
            }
            size="small"
            className="organizer-card"
          >
            <Row gutter={16}>
              <Col span={12}>
                <Statistic 
                  title="总任务" 
                  value={tasks.length} 
                  valueStyle={{ color: 'var(--theme-primary)', fontSize: 24 }}
                />
              </Col>
              <Col span={12}>
                <Statistic 
                  title="已完成" 
                  value={tasks.filter(t => t.status === 'completed').length}
                  valueStyle={{ color: 'var(--color-success)', fontSize: 24 }}
                />
              </Col>
            </Row>
          </Card>
        )}
      </div>

      {/* 右侧：任务列表 */}
      <div className="organizer-main">
        {tasks.length === 0 ? (
          <Empty
            image={<FileSearchOutlined style={{ fontSize: 64, color: 'var(--text-muted)' }} />}
            description={
              <Space direction="vertical" size="small">
                <Text type="secondary">暂无整理任务</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  在左侧添加角色名称和文件夹名称来创建任务
                </Text>
              </Space>
            }
          />
        ) : (
          <>
            <div className="organizer-actions">
              <Space>
                <Button
                  type="primary"
                  icon={<PlayCircleOutlined />}
                  onClick={handleExecuteAll}
                  loading={isProcessing}
                  disabled={tasks.every(t => t.status !== 'pending')}
                >
                  执行所有任务
                </Button>
                <Button
                  icon={<ReloadOutlined />}
                  onClick={() => {
                    setTasks([])
                    setCurrentTaskId(null)
                  }}
                  disabled={isProcessing}
                >
                  清空列表
                </Button>
              </Space>
            </div>

            <List
              className="organizer-task-list"
              itemLayout="vertical"
              dataSource={tasks}
              renderItem={(task) => (
                <List.Item
                  className={`organizer-task-item ${currentTaskId === task.id ? 'active' : ''}`}
                >
                  <Card 
                    size="small"
                    className="task-card"
                    title={
                      <Space>
                        <Badge status={getStatusColor(task.status) as any} />
                        {editingTask === task.id ? (
                          <Space>
                            <Input
                              size="small"
                              value={editFolderName}
                              onChange={(e) => setEditFolderName(e.target.value)}
                              style={{ width: 150 }}
                            />
                            <Button 
                              type="text" 
                              size="small" 
                              icon={<CheckOutlined />}
                              onClick={() => handleSaveEdit(task.id)}
                            />
                            <Button 
                              type="text" 
                              size="small" 
                              icon={<CloseOutlined />}
                              onClick={handleCancelEdit}
                            />
                          </Space>
                        ) : (
                          <>
                            <Text strong>{task.folderName}</Text>
                            {task.status === 'pending' && (
                              <Button
                                type="text"
                                size="small"
                                icon={<EditOutlined />}
                                onClick={() => handleStartEdit(task)}
                              />
                            )}
                          </>
                        )}
                      </Space>
                    }
                    extra={
                      <Space>
                        <Tag color={getStatusColor(task.status)}>
                          {getStatusText(task.status)}
                        </Tag>
                        {task.status === 'pending' && (
                          <Tooltip title="删除">
                            <Button
                              type="text"
                              danger
                              size="small"
                              icon={<DeleteOutlined />}
                              onClick={() => handleRemoveTask(task.id)}
                              disabled={isProcessing}
                            />
                          </Tooltip>
                        )}
                      </Space>
                    }
                  >
                    {/* 角色标签 */}
                    <div style={{ marginBottom: 12 }}>
                      <Space size={4} wrap>
                        {task.charNames.map((name, idx) => (
                          <Tag key={idx} color="blue">
                            <UserOutlined style={{ marginRight: 4 }} />
                            {name}
                          </Tag>
                        ))}
                      </Space>
                    </div>

                    {/* 进度显示 */}
                    {(task.status === 'scanning' || task.status === 'analyzing' || task.status === 'moving') && (
                      <div style={{ marginBottom: 12 }}>
                        <Progress 
                          percent={task.progress} 
                          size="small" 
                          status="active"
                          format={(percent) => `${percent}%`}
                        />
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {task.status === 'scanning' && '正在扫描场景文件...'}
                          {task.status === 'analyzing' && `正在分析场景 (${task.processedScenes}/${task.totalScenes})...`}
                          {task.status === 'moving' && `正在移动文件 (${task.results.length} 个场景)...`}
                        </Text>
                      </div>
                    )}

                    {/* 完成结果 */}
                    {task.status === 'completed' && (
                      <Alert
                        message={`成功整理 ${task.results.length} 个场景到 "hs_tools_${task.folderName}"`}
                        type="success"
                        showIcon
                        style={{ fontSize: 12 }}
                      />
                    )}

                    {/* 错误信息 */}
                    {task.status === 'error' && task.error && (
                      <Alert
                        message={task.error}
                        type="error"
                        showIcon
                        style={{ fontSize: 12 }}
                      />
                    )}

                    {/* 待执行操作 */}
                    {task.status === 'pending' && (
                      <Button
                        type="primary"
                        size="small"
                        icon={<PlayCircleOutlined />}
                        onClick={() => executeTask(task)}
                        disabled={isProcessing}
                      >
                        执行此任务
                      </Button>
                    )}
                  </Card>
                </List.Item>
              )}
            />
          </>
        )}
      </div>
    </div>
  )
}
